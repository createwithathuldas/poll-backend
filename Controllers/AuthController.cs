using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PollApi.Data;
using PollApi.DTOs.Request;
using PollApi.DTOs.Response;
using PollApi.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace PollApi.Controllers
{
    [ApiController]
    [Route("api/auth")]
    [Produces("application/json")]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _cfg;

        public AuthController(ApplicationDbContext db, IConfiguration cfg)
        {
            _db = db;
            _cfg = cfg;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  USER
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Register a new regular user.
        /// Role is forced to "User" — cannot be elevated here.
        /// </summary>
        /// <remarks>
        /// POST /api/auth/user/register
        ///
        ///     {
        ///       "name": "John Doe",
        ///       "email": "john@example.com",
        ///       "password": "Secret@123"
        ///     }
        /// </remarks>
        [HttpPost("user/register")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ApiResponse<AuthResponse>), 200)]
        [ProducesResponseType(typeof(ApiResponse<object>), 400)]
        public async Task<IActionResult> UserRegister([FromBody] RegisterRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(Fail(ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList()));

            if (await _db.Users.AnyAsync(u => u.Email == req.Email))
                return BadRequest(Fail("Email already registered."));

            var user = new User
            {
                Name     = req.Name,
                Email    = req.Email,
                Role     = "User",                                 // hard-locked
                Password = BCrypt.Net.BCrypt.HashPassword(req.Password),
                IsDeleted = false,
                IsKicked  = false
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            return Ok(Success(BuildAuthResponse(user), "Registration successful."));
        }

        /// <summary>
        /// Login as a regular user.
        /// Returns JWT + refresh token. Kicked users are blocked.
        /// </summary>
        /// <remarks>
        /// POST /api/auth/user/login
        ///
        ///     {
        ///       "email": "john@example.com",
        ///       "password": "Secret@123"
        ///     }
        /// </remarks>
        [HttpPost("user/login")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ApiResponse<AuthResponse>), 200)]
        [ProducesResponseType(typeof(ApiResponse<object>), 401)]
        public async Task<IActionResult> UserLogin([FromBody] LoginRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(Fail(ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList()));

            var user = await _db.Users
                .IgnoreQueryFilters()                              // include soft-deleted check
                .FirstOrDefaultAsync(u => u.Email == req.Email && !u.IsDeleted);

            if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.Password))
                return Unauthorized(Fail("Invalid email or password."));

            if (user.Role != "User")
                return Unauthorized(Fail("Use admin login for admin accounts."));

            if (user.IsKicked)
                return Unauthorized(Fail($"Account suspended. Reason: {user.KickReason}"));

            return Ok(Success(BuildAuthResponse(user)));
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ADMIN
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Register a new admin.
        /// Requires an existing admin JWT — only admins can create other admins.
        /// The admin-secret header adds a second layer for bootstrap protection.
        /// </summary>
        /// <remarks>
        /// POST /api/auth/admin/register
        /// Headers: Authorization: Bearer {admin_jwt}
        ///          X-Admin-Secret: {value from appsettings AdminSecret}
        ///
        ///     {
        ///       "name": "Super Admin",
        ///       "email": "admin2@fifapoll.com",
        ///       "password": "Admin@9876"
        ///     }
        ///
        /// Note: Only ONE admin is seeded at startup (via ApplicationDbContext).
        /// Additional admins must be created through this endpoint by an existing admin.
        /// </remarks>
        [HttpPost("admin/register")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(ApiResponse<AuthResponse>), 200)]
        [ProducesResponseType(typeof(ApiResponse<object>), 400)]
        [ProducesResponseType(typeof(ApiResponse<object>), 403)]
        public async Task<IActionResult> AdminRegister(
            [FromBody] RegisterRequest req,
            [FromHeader(Name = "X-Admin-Secret")] string? adminSecret)
        {
            // Double-lock: valid JWT (Admin role) + secret header must match
            var expectedSecret = _cfg["AdminSecret"];
            if (string.IsNullOrEmpty(expectedSecret) || adminSecret != expectedSecret)
                return StatusCode(403, Fail("Invalid admin secret."));

            if (!ModelState.IsValid)
                return BadRequest(Fail(ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList()));

            if (await _db.Users.AnyAsync(u => u.Email == req.Email))
                return BadRequest(Fail("Email already registered."));

            var admin = new User
            {
                Name     = req.Name,
                Email    = req.Email,
                Role     = "Admin",                                // hard-locked
                Password = BCrypt.Net.BCrypt.HashPassword(req.Password),
                IsDeleted = false,
                IsKicked  = false
            };

            _db.Users.Add(admin);
            await _db.SaveChangesAsync();

            return Ok(Success(BuildAuthResponse(admin), "Admin registered successfully."));
        }

        /// <summary>
        /// Login as admin.
        /// Validates role == "Admin" strictly — regular users are blocked.
        /// </summary>
        /// <remarks>
        /// POST /api/auth/admin/login
        ///
        ///     {
        ///       "email": "admin@fifapoll.com",
        ///       "password": "Admin@1234"
        ///     }
        /// </remarks>
        [HttpPost("admin/login")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ApiResponse<AuthResponse>), 200)]
        [ProducesResponseType(typeof(ApiResponse<object>), 401)]
        public async Task<IActionResult> AdminLogin([FromBody] LoginRequest req)
        {
            if (!ModelState.IsValid)
                return BadRequest(Fail(ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList()));

            var admin = await _db.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Email == req.Email && !u.IsDeleted);

            if (admin == null || !BCrypt.Net.BCrypt.Verify(req.Password, admin.Password))
                return Unauthorized(Fail("Invalid email or password."));

            if (admin.Role != "Admin")
                return Unauthorized(Fail("This account does not have admin privileges."));

            return Ok(Success(BuildAuthResponse(admin)));
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SHARED
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Refresh JWT using a valid refresh token.
        /// Refresh tokens are stored in Redis (wire up IRefreshTokenStore).
        /// </summary>
        [HttpPost("refresh")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ApiResponse<AuthResponse>), 200)]
        [ProducesResponseType(typeof(ApiResponse<object>), 401)]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest req)
        {
            // TODO: validate refresh token from Redis store
            // Stub returns 401 until Redis wired
            await Task.CompletedTask;
            return Unauthorized(Fail("Refresh token invalid or expired."));
        }

        /// <summary>Revoke refresh token on logout.</summary>
        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest req)
        {
            // TODO: delete refresh token from Redis store
            await Task.CompletedTask;
            return Ok(Success<object>(null!, "Logged out successfully."));
        }

        // ════════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════════════════════════

        private AuthResponse BuildAuthResponse(User user)
        {
            var key    = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg["Jwt:Secret"]!));
            var creds  = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name,           user.Name),
                new Claim(ClaimTypes.Email,          user.Email),
                new Claim(ClaimTypes.Role,           user.Role)
            };

            var token = new JwtSecurityToken(
                issuer:             _cfg["Jwt:Issuer"],
                audience:           _cfg["Jwt:Audience"],
                claims:             claims,
                expires:            DateTime.UtcNow.AddHours(8),
                signingCredentials: creds);

            return new AuthResponse
            {
                Token        = new JwtSecurityTokenHandler().WriteToken(token),
                RefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
                User = new UserResponse
                {
                    Id         = user.Id,
                    Name       = user.Name,
                    Email      = user.Email,
                    Role       = user.Role,
                    IsKicked   = user.IsKicked,
                    KickReason = user.KickReason
                }
            };
        }

        private static ApiResponse<T> Success<T>(T data, string? msg = null) =>
            new() { Success = true, Data = data, Message = msg };

        private static ApiResponse<object> Fail(string error) =>
            new() { Success = false, Errors = new List<string> { error } };

        private static ApiResponse<object> Fail(List<string> errors) =>
            new() { Success = false, Errors = errors };
    }

    // ── DTOs scoped to auth (avoid cross-file dependency on separate DTOs file) ─

    public class RefreshTokenRequest
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string RefreshToken { get; set; } = string.Empty;
    }
}