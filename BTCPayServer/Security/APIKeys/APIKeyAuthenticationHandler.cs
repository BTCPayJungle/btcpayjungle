﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Security.Bitpay;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Security.APIKeys
{
    public class APIKeyAuthenticationHandler : AuthenticationHandler<APIKeyAuthenticationOptions>
    {
        private readonly APIKeyRepository _apiKeyRepository;
        private readonly IOptionsMonitor<IdentityOptions> _identityOptions;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public APIKeyAuthenticationHandler(
            APIKeyRepository apiKeyRepository,
            IOptionsMonitor<IdentityOptions> identityOptions,
            IOptionsMonitor<APIKeyAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager) : base(options, logger, encoder, clock)
        {
            _apiKeyRepository = apiKeyRepository;
            _identityOptions = identityOptions;
            _signInManager = signInManager;
            _userManager = userManager;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var res = await HandleApiKeyAuthenticateResult();
            if (res.None)
            {
                return await HandleBasicAuthenticateAsync();
            }

            return res;
        }

        private async Task<AuthenticateResult> HandleApiKeyAuthenticateResult()
        {
            if (!Context.Request.HttpContext.GetAPIKey(out var apiKey) || string.IsNullOrEmpty(apiKey))
                return AuthenticateResult.NoResult();

            var key = await _apiKeyRepository.GetKey(apiKey);

            if (key == null)
            {
                return AuthenticateResult.Fail("ApiKey authentication failed");
            }

            List<Claim> claims = new List<Claim>();
            claims.Add(new Claim(_identityOptions.CurrentValue.ClaimsIdentity.UserIdClaimType, key.UserId));
            claims.AddRange(Permission.ToPermissions(key.Permissions).Select(permission =>
                new Claim(APIKeyConstants.ClaimTypes.Permission, permission.ToString())));
            return AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, APIKeyConstants.AuthenticationType)),
                APIKeyConstants.AuthenticationType));
        }

        private async Task<AuthenticateResult> HandleBasicAuthenticateAsync()
        {
            string authHeader = Context.Request.Headers["Authorization"];

            if (authHeader == null || !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
            var encodedUsernamePassword = authHeader.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[1]?.Trim();
            var decodedUsernamePassword =
                Encoding.UTF8.GetString(Convert.FromBase64String(encodedUsernamePassword)).Split(':');
            var username = decodedUsernamePassword[0];
            var password = decodedUsernamePassword[1];

            var result = await _signInManager.PasswordSignInAsync(username, password, true, true);
            if (!result.Succeeded) return AuthenticateResult.Fail(result.ToString());

            var user = await _userManager.FindByNameAsync(username);
            var claims = new List<Claim>()
            {
                new Claim(_identityOptions.CurrentValue.ClaimsIdentity.UserIdClaimType, user.Id),
                new Claim(APIKeyConstants.ClaimTypes.Permission,
                    Permission.Create(Policies.Unrestricted).ToString())
            };

            return AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, APIKeyConstants.AuthenticationType)),
                APIKeyConstants.AuthenticationType));
        }
    }
}
