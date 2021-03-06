﻿using API.DTO.User;
using API.Services;
using API_DataAccess.DataAccess.Contracts;
using API_DataAccess.DataAccess.Core;
using API_DataAccess.Model;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using API.Helpers;
using EmailService;

namespace API.Controllers
{
    [Route("api/users")]
    [ApiController]
    public class UsersController : BaseController
    {
        private readonly ILogger<UsersController> _log;
        private readonly IMapper _mapper;
        private readonly IUserData _userData;
        private readonly IUserService _userService;
        private readonly IUserAuthService _userAuthService;

        SmtpSettings _smtpSettings;

        public UsersController(ILogger<UsersController> log,
                                IMapper mapper,
                                IUserData userData,
                                IUserService userService,
                                IUserAuthService userAuthService,
                                SmtpSettings smtpSettings)
        {
            _log = log;
            _userData = userData;
            _userService = userService;
            _userAuthService = userAuthService;
            _mapper = mapper;
            _smtpSettings = smtpSettings;
        }

        [HttpPost("authenticate")]
        public ActionResult<ReadUserDTO> Authenticate([FromBody] LoginDTO loginDTO)
        {
            var response = _userAuthService.Authenticate(loginDTO, ipAddress());

            if (response == null)
                return BadRequest(new { message = "Username or password is incorrect" });

            setTokenCookie(response.RefreshToken);

            return Ok(response);
        }

        [HttpPost("refresh-token")]
        public ActionResult<ReadUserDTO> RefreshToken()
        {
            var refreshToken = Request.Cookies["refreshToken"];
            var response = _userAuthService.RefreshToken(refreshToken, ipAddress());

            if (response == null)
                return Unauthorized(new { message = "Invalid token" });

            setTokenCookie(response.RefreshToken);
            return Ok(response);
        }

        [Authorize]
        [HttpPost("revoke-token")]
        public ActionResult RevokeToken([FromBody] RevokeTokenRequestDTO revokeTokenInput)
        {
            var token = revokeTokenInput.RefreshToken ?? System.Net.WebUtility.UrlDecode(Request.Cookies["refreshToken"]);

            if (string.IsNullOrEmpty(token))
                return BadRequest(new { message = "Token is required" });

            var response = _userAuthService.RevokeToken(token, ipAddress());

            if (!response)
                return NotFound(new { message = "Token not found - " + token });

            return Ok(new { message = "Token revoked" });
        }


        [HttpGet]
        [Authorize(RoleKey.admin)] // , RoleKey.superuser
        public ActionResult<IEnumerable<ReadUserDTO>> Get()
        {
            var users = _userData.GetAll_exclude_deleted();

            var usersDTO = _mapper.Map<IEnumerable<UserBaseDTO>>(users);
            return Ok(usersDTO);
        }


        [HttpGet("{id}")]
        [Authorize] //(RoleKey.admin) -> authorize lang since, i allowed namn ung normal user ma view ung details nya
        public ActionResult<int> GetById(long id)
        {
            if (UserDetails != null && id != UserDetails.Id && CheckIfUserHasAdminRole(UserDetails.Roles) == false)
                return Unauthorized(new { message = "Unauthorized" });

            // only allow admins to access other user records
            //var currentUserId = int.Parse(User.Identity.Name);
            //if (id != currentUserId && !User.IsInRole("admin"))
            //    return Forbid();

            var user = _userData.GetById(id);

             var userDTO = _mapper.Map<UserBaseDTO>(user);
            return Ok(userDTO);
        }

        [HttpPost("register")]
        public async Task<ActionResult<ReadUserDTO>> Register(RegisterUserRequestDTO model)
        {
            try
            {
                await _userService.Register(model, Request.Headers["origin"]);
                return Ok(new { message = "Registration successful, please check your email for verification instructions" });
            }
            catch (AppException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

        }

        [HttpPost("verify-email")]
        public ActionResult VerifyEmail(VerifyEmailRequestDTO model)
        {
            _userService.VerifyEmail(model.Token);
            return Ok(new { message = "Verification successful, you can now login" });
        }

        [Authorize(RoleKey.admin)]
        [HttpPost("create")]
        public ActionResult<ReadUserDTO> Create(CreateUserRequestDTO model)
        {
            try
            {
                var user = _userService.Create(model);
                return Ok(user);
            }
            catch(AppException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            
        }

        [Authorize(RoleKey.admin)]
        [HttpPut("update/{id:int}")]
        public ActionResult<ReadUserDTO> Update(long id, UpdateUserRequestDTO model)
        {
            try
            {
                var user = _userService.Update(id, model);
                return Ok(user);
            }
            catch (AppException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

        }

        [HttpPost("forgot-password")]
        public async Task<ActionResult> ForgotPassword (ForgotPasswordRequestDTO model)
        {
            await _userService.ForgotPassword(model, Request.Headers["origin"]);
            return Ok(new { message = $"Please check your email for password reset instructions {_smtpSettings.EmailFrom}" });
        }


        [HttpPost("validate-reset-token")]
        public ActionResult ValidateResetToken(ValidateResetTokenRequestDTO model)
        {
            try
            {
                if (_userService.ValidateResetToken(model))
                    return Ok(new { message = "Token is valid" });
            }
            catch (AppException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            return BadRequest(new { message = "Invalid token" });
        }

        [HttpPost("reset-password")]
        public ActionResult ResetPassword(ResetPasswordRequestDTO model)
        {
            try
            {
                if (_userService.ResetPassword(model))
                    return Ok(new { message = "Password reset successful, you can now login" });
            }
            catch (AppException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            return BadRequest(new { message = "Invalid token" });
        }

        [Authorize(RoleKey.admin)]
        [HttpDelete("delete/{id:int}")]
        public ActionResult Delete(long id)
        {
            try
            {
                _userService.Delete(id);
                return Ok(new { message = "Account deleted successfully" });
            }
            catch (AppException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // helper methods

        private void setTokenCookie(string token)
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Expires = DateTime.UtcNow.AddDays(7)
            };
            Response.Cookies.Append("refreshToken", token, cookieOptions);
        }

        private string ipAddress()
        {
            if (Request.Headers.ContainsKey("X-Forwarded-For"))
                return Request.Headers["X-Forwarded-For"];
            else
                return HttpContext.Connection.RemoteIpAddress.MapToIPv4().ToString();
        }

    }
}
