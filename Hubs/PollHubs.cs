using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PollApi.DTOs.Request;
using PollApi.Services.Interfaces;
using System.Security.Claims;

namespace PollApi.Hubs
{
    [Authorize]
    public class PollHub : Hub
    {
        private readonly IChatService _chat;
        private readonly IUserService _user;

        public PollHub(IChatService chat, IUserService user)
        {
            _chat = chat;
            _user = user;
        }

        // ── Connection lifecycle ─────────────────────────────────────────────────

        public override async Task OnConnectedAsync()
        {
            var userId = GetUserId();
            var role = GetRole();

            // Per-user group → for targeted kick / notification delivery
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");

            if (role == "Admin")
                await Groups.AddToGroupAsync(Context.ConnectionId, "admins");

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = GetUserId();
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user-{userId}");
            await base.OnDisconnectedAsync(exception);
        }

        // ── Poll room join / leave ───────────────────────────────────────────────

        public async Task JoinPollRoom(int pollId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"poll-{pollId}");
            await Clients.Group($"poll-{pollId}")
                         .SendAsync("UserJoined", new { UserId = GetUserId(), PollId = pollId });
        }

        public async Task LeavePollRoom(int pollId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"poll-{pollId}");
        }

        // ── Chat ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Client sends: { message, pollId? }
        /// Server saves → broadcasts to correct room (or global).
        /// </summary>
        public async Task SendMessage(SendChatRequest req)
        {
            var userId = GetUserId();

            // Kick-check: kicked users cannot chat
            var userResp = await _user.GetByIdAsync(userId);
            if (userResp.IsKicked)
            {
                await Clients.Caller.SendAsync("Error", "You have been removed and cannot chat.");
                return;
            }

            var saved = await _chat.SaveMessageAsync(req, userId);

            var group = req.PollId.HasValue
                ? $"poll-{req.PollId}"
                : "global-chat";

            await Clients.Group(group).SendAsync("NewMessage", saved);
        }

        // ── Admin: delete a chat message live ────────────────────────────────────

        public async Task DeleteMessage(int messageId, string reason)
        {
            if (GetRole() != "Admin")
            {
                await Clients.Caller.SendAsync("Error", "Unauthorized.");
                return;
            }

            await _chat.DeleteMessageAsync(messageId, GetUserId(), reason);
            await Clients.All.SendAsync("MessageDeleted", new { MessageId = messageId, Reason = reason });
        }

        // ── Admin: live vote count push ──────────────────────────────────────────

        /// <summary>
        /// Admin can trigger a push of current (non-revealed) vote counts.
        /// Admin-only; hides actual counts from regular users until reveal.
        /// </summary>
        public async Task PushAdminVoteUpdate(int pollId, object payload)
        {
            if (GetRole() != "Admin") return;
            await Clients.Group("admins").SendAsync("AdminVoteUpdate", payload);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private int GetUserId() =>
            int.Parse(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private string GetRole() =>
            Context.User!.FindFirstValue(ClaimTypes.Role) ?? "User";
    }
}