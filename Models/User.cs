namespace PollApi.Models{
    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
        public string Password { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsKicked { get; set; }
        public string? KickReason { get; set; }
        public DateTime? KickedAt { get; set; }
 
        // Nav
        public ICollection<Vote> Votes { get; set; }
        public ICollection<ChatMessage> ChatMessages { get; set; }
        public ICollection<Notification> Notifications { get; set; }
    }
}