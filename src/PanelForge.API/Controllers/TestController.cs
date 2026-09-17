using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PanelForge.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TestAuthController : ControllerBase
    {
        // 1. API không cần đăng nhập (Public)
        [HttpGet("public-data")]
        public IActionResult GetPublicData()
        {
            return Ok(new { message = "Ai cũng có thể xem dòng này (Không cần đăng nhập)." });
        }

        // 2. API YÊU CẦU ĐĂNG NHẬP (Private) - Có gắn thẻ [Authorize]
        [Authorize]
        [HttpGet("private-data")]
        public IActionResult GetPrivateData()
        {
            // Lấy UID (User ID) của Firebase từ Token
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            return Ok(new
            {
                message = "Bạn đã vượt qua chốt chặn bảo mật thành công!",
                firebaseUserId = userId
            });
        }
    }
}