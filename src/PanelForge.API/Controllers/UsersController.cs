using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.DTOs.Users;
using PanelForge.Application.Users.Commands.SoftDeleteUser;
using PanelForge.Application.Users.Commands.UpdateUserProfile;
using PanelForge.Application.Users.Queries.GetUserProfile;

namespace PanelForge.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly ISender _sender;

    public UsersController(ISender sender)
    {
        _sender = sender;
    }

    private string GetCallerFirebaseUid()
    {
        return User.FindFirstValue("firebase_uid")
            ?? User.FindFirstValue("user_id")
            ?? User.FindFirstValue("sub")
            ?? string.Empty;
    }

    private string? GetCallerUserIdStr()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
    }

    private bool IsCallerAdmin()
    {
        return User.IsInRole("Admin") || User.FindFirstValue(ClaimTypes.Role) == "Admin";
    }

    /// <summary>
    /// Lấy thông tin hồ sơ của chính người dùng đang đăng nhập
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile(CancellationToken cancellationToken)
    {
        var userIdStr = GetCallerUserIdStr();
        var firebaseUid = GetCallerFirebaseUid();

        Guid? guid = Guid.TryParse(userIdStr, out var parsedGuid) ? parsedGuid : null;
        var query = new GetUserProfileQuery(Id: guid, FirebaseUid: firebaseUid);

        var result = await _sender.Send(query, cancellationToken);
        if (result is null)
        {
            return NotFound(new { message = "Không tìm thấy thông tin hồ sơ của bạn." });
        }

        return Ok(result);
    }

    /// <summary>
    /// Lấy chi tiết người dùng theo PostgreSQL Id
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var query = new GetUserProfileQuery(Id: id);
        var result = await _sender.Send(query, cancellationToken);

        if (result is null)
        {
            return NotFound(new { message = $"Không tìm thấy người dùng với Id: {id}" });
        }

        return Ok(result);
    }

    /// <summary>
    /// Lấy chi tiết người dùng theo Firebase Uid
    /// </summary>
    [HttpGet("firebase/{firebaseUid}")]
    public async Task<IActionResult> GetByFirebaseUid(string firebaseUid, CancellationToken cancellationToken)
    {
        var query = new GetUserProfileQuery(FirebaseUid: firebaseUid);
        var result = await _sender.Send(query, cancellationToken);

        if (result is null)
        {
            return NotFound(new { message = $"Không tìm thấy người dùng với FirebaseUid: {firebaseUid}" });
        }

        return Ok(result);
    }

    /// <summary>
    /// Cập nhật Username và Avatar theo PostgreSQL Id (xác thực quyền sở hữu qua token)
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateProfile(Guid id, [FromBody] UpdateUserProfileRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var command = new UpdateUserProfileCommand(
                TargetUserId: id,
                TargetFirebaseUid: null,
                CurrentUserFirebaseUid: GetCallerFirebaseUid(),
                CurrentUserIdStr: GetCallerUserIdStr(),
                Username: request.Username,
                Avatar: request.Avatar,
                PhoneNumber: request.PhoneNumber,
                IsAdmin: IsCallerAdmin()
            );

            var result = await _sender.Send(command, cancellationToken);
            if (result is null)
            {
                return NotFound(new { message = $"Không tìm thấy người dùng với Id: {id}" });
            }

            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Cập nhật Username và Avatar theo Firebase Uid (xác thực quyền sở hữu qua token)
    /// </summary>
    [HttpPut("firebase/{firebaseUid}")]
    public async Task<IActionResult> UpdateProfileByFirebaseUid(string firebaseUid, [FromBody] UpdateUserProfileRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var command = new UpdateUserProfileCommand(
                TargetUserId: null,
                TargetFirebaseUid: firebaseUid,
                CurrentUserFirebaseUid: GetCallerFirebaseUid(),
                CurrentUserIdStr: GetCallerUserIdStr(),
                Username: request.Username,
                Avatar: request.Avatar,
                PhoneNumber: request.PhoneNumber,
                IsAdmin: IsCallerAdmin()
            );

            var result = await _sender.Send(command, cancellationToken);
            if (result is null)
            {
                return NotFound(new { message = $"Không tìm thấy người dùng với FirebaseUid: {firebaseUid}" });
            }

            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Vô hiệu hóa (Soft Delete) người dùng theo PostgreSQL Id
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var command = new SoftDeleteUserCommand(
                TargetUserId: id,
                TargetFirebaseUid: null,
                CurrentUserFirebaseUid: GetCallerFirebaseUid(),
                CurrentUserIdStr: GetCallerUserIdStr(),
                IsAdmin: IsCallerAdmin()
            );

            var result = await _sender.Send(command, cancellationToken);
            if (result is null)
            {
                return NotFound(new { message = $"Không tìm thấy người dùng với Id: {id}" });
            }

            return Ok(new { message = "Vô hiệu hóa (Soft delete) người dùng thành công." });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Vô hiệu hóa (Soft Delete) người dùng theo Firebase Uid
    /// </summary>
    [HttpDelete("firebase/{firebaseUid}")]
    public async Task<IActionResult> SoftDeleteByFirebaseUid(string firebaseUid, CancellationToken cancellationToken)
    {
        try
        {
            var command = new SoftDeleteUserCommand(
                TargetUserId: null,
                TargetFirebaseUid: firebaseUid,
                CurrentUserFirebaseUid: GetCallerFirebaseUid(),
                CurrentUserIdStr: GetCallerUserIdStr(),
                IsAdmin: IsCallerAdmin()
            );

            var result = await _sender.Send(command, cancellationToken);
            if (result is null)
            {
                return NotFound(new { message = $"Không tìm thấy người dùng với FirebaseUid: {firebaseUid}" });
            }

            return Ok(new { message = "Vô hiệu hóa (Soft delete) người dùng thành công." });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
