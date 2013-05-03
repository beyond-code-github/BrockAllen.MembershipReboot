﻿using System;
using System.IdentityModel.Services;
using System.IdentityModel.Tokens;
using System.Linq;
using System.Security.Claims;

namespace BrockAllen.MembershipReboot
{
    public class ClaimsBasedAuthenticationService : ClaimsBasedAuthenticationService<UserAccount, int>
    {
        public ClaimsBasedAuthenticationService(UserAccountService userService)
            : base(userService)
        {
        }
    }

    public class ClaimsBasedAuthenticationService<T, TKey> : IDisposable
        where T : UserAccount<TKey>, new()
    {
        UserAccountService<T, TKey> userService;

        public ClaimsBasedAuthenticationService(UserAccountService<T, TKey> userService)
        {
            this.userService = userService;
        }

        public void Dispose()
        {
            if (this.userService != null)
            {
                this.userService.Dispose();
                this.userService = null;
            }
        }

        public virtual void SignIn(string username)
        {
            SignIn(null, username);
        }

        public virtual void SignIn(string tenant, string username)
        {
            Tracing.Information(String.Format("[ClaimsBasedAuthenticationService.Signin] called: {0}, {1}", tenant, username));

            if (!SecuritySettings.Instance.MultiTenant)
            {
                tenant = SecuritySettings.Instance.DefaultTenant;
            }

            if (String.IsNullOrWhiteSpace(tenant)) throw new ArgumentException("tenant");
            if (String.IsNullOrWhiteSpace(username)) throw new ArgumentException("username");

            // find user
            var account = this.userService.GetByUsername(tenant, username);
            if (account == null) throw new ArgumentException("Invalid username");

            // gather claims
            var claims =
                (from uc in account.Claims
                 select new Claim(uc.Type, uc.Value)).ToList();

            if (!String.IsNullOrWhiteSpace(account.Email))
            {
                claims.Insert(0, new Claim(ClaimTypes.Email, account.Email));
            }
            claims.Insert(0, new Claim(ClaimTypes.AuthenticationMethod, AuthenticationMethods.Password));
            claims.Insert(0, new Claim(ClaimTypes.AuthenticationInstant, DateTime.UtcNow.ToString("s")));
            claims.Insert(0, new Claim(ClaimTypes.Name, account.Username));
            claims.Insert(0, new Claim(MembershipRebootConstants.ClaimTypes.Tenant, account.Tenant));
            claims.Insert(0, new Claim(ClaimTypes.NameIdentifier, account.NameID.ToString("D")));

            // create principal/identity
            var id = new ClaimsIdentity(claims, "Forms");
            var cp = new ClaimsPrincipal(id);

            // claims transform
            cp = FederatedAuthentication.FederationConfiguration.IdentityConfiguration.ClaimsAuthenticationManager.Authenticate(String.Empty, cp);

            // issue cookie
            var sam = FederatedAuthentication.SessionAuthenticationModule;
            if (sam == null)
            {
                Tracing.Verbose("[ClaimsBasedAuthenticationService.Signin] SessionAuthenticationModule is not configured");
                throw new Exception("SessionAuthenticationModule is not configured and it needs to be.");
            }

            var handler = FederatedAuthentication.FederationConfiguration.IdentityConfiguration.SecurityTokenHandlers[typeof(SessionSecurityToken)] as SessionSecurityTokenHandler;
            if (handler == null)
            {
                Tracing.Verbose("[ClaimsBasedAuthenticationService.Signin] SessionSecurityTokenHandler is not configured");
                throw new Exception("SessionSecurityTokenHandler is not configured and it needs to be.");
            }

            var token = new SessionSecurityToken(cp, handler.TokenLifetime);
            token.IsPersistent = FederatedAuthentication.FederationConfiguration.WsFederationConfiguration.PersistentCookiesOnPassiveRedirects;
            token.IsReferenceMode = sam.IsReferenceMode;

            sam.WriteSessionTokenToCookie(token);

            Tracing.Verbose(String.Format("[ClaimsBasedAuthenticationService.Signin] cookie issued: {0}", claims.GetValue(ClaimTypes.NameIdentifier)));
        }

        public virtual void SignOut()
        {
            Tracing.Information(String.Format("[ClaimsBasedAuthenticationService.SignOut] called: {0}", ClaimsPrincipal.Current.Claims.GetValue(ClaimTypes.NameIdentifier)));

            // clear cookie
            var sam = FederatedAuthentication.SessionAuthenticationModule;
            if (sam == null)
            {
                Tracing.Verbose("[ClaimsBasedAuthenticationService.Signout] SessionAuthenticationModule is not configured");
                throw new Exception("SessionAuthenticationModule is not configured and it needs to be.");
            }

            sam.SignOut();
        }
    }
}
