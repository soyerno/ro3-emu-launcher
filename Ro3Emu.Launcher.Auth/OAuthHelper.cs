using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ro3Emu.Launcher.Auth;

public static class OAuthHelper
{
	private const int CallbackPort = 5385;

	private const string RedirectUri = "http://127.0.0.1:5385/callback/";

	public static string GoogleClientId { get; set; } = "";

	public static string MicrosoftClientId { get; set; } = "";

	public static async Task<AuthResult> StartGoogleAuthAsync(CancellationToken cancel = default(CancellationToken))
	{
		return await RunOAuthFlowAsync("google", "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token", "https://www.googleapis.com/oauth2/v3/userinfo", GoogleClientId, "openid profile email", cancel).ConfigureAwait(continueOnCapturedContext: false);
	}

	public static async Task<AuthResult> StartMicrosoftAuthAsync(CancellationToken cancel = default(CancellationToken))
	{
		return await RunOAuthFlowAsync("microsoft", "https://login.microsoftonline.com/common/oauth2/v2.0/authorize", "https://login.microsoftonline.com/common/oauth2/v2.0/token", "https://graph.microsoft.com/v1.0/me", MicrosoftClientId, "openid profile email User.Read", cancel).ConfigureAwait(continueOnCapturedContext: false);
	}

	private static async Task<AuthResult> RunOAuthFlowAsync(string provider, string authEndpoint, string tokenEndpoint, string userInfoEndpoint, string clientId, string scopes, CancellationToken cancel)
	{
		if (string.IsNullOrWhiteSpace(clientId))
		{
			return await PromptOrSimulateDevAuthAsync(provider).ConfigureAwait(continueOnCapturedContext: false);
		}
		string state = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
		string codeVerifier = GenerateCodeVerifier();
		string value = GenerateCodeChallenge(codeVerifier);
		using HttpListener listener = new HttpListener();
		listener.Prefixes.Add("http://127.0.0.1:5385/callback/");
		try
		{
			listener.Start();
		}
		catch (HttpListenerException ex)
		{
			return new AuthResult(Success: false, $"No se pudo iniciar el escucha local de autenticación (puerto {5385} ocupado): {ex.Message}");
		}
		try
		{
			Process.Start(new ProcessStartInfo($"{authEndpoint}?client_id={Uri.EscapeDataString(clientId)}&response_type=code&redirect_uri={Uri.EscapeDataString("http://127.0.0.1:5385/callback/")}&scope={Uri.EscapeDataString(scopes)}&state={state}&code_challenge={value}&code_challenge_method=S256&prompt=select_account")
			{
				UseShellExecute = true
			});
			using CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2L));
			using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeoutCts.Token);
			Task<HttpListenerContext> contextTask = listener.GetContextAsync();
			if (await Task.WhenAny(contextTask, Task.Delay(-1, linkedCts.Token)).ConfigureAwait(continueOnCapturedContext: false) != contextTask)
			{
				return new AuthResult(Success: false, "El tiempo de espera para iniciar sesión con " + provider + " ha expirado o fue cancelado.");
			}
			HttpListenerContext httpListenerContext = await contextTask.ConfigureAwait(continueOnCapturedContext: false);
			NameValueCollection queryString = httpListenerContext.Request.QueryString;
			string code = queryString["code"];
			string receivedState = queryString["state"];
			string error = queryString["error"];
			await SendBrowserResponseAsync(httpListenerContext.Response, error == null && code != null, provider).ConfigureAwait(continueOnCapturedContext: false);
			if (!string.IsNullOrEmpty(error))
			{
				return new AuthResult(Success: false, "Inicio de sesión cancelado o denegado por " + provider + ": " + error);
			}
			if (receivedState != state || string.IsNullOrEmpty(code))
			{
				return new AuthResult(Success: false, "Fallo de validación de seguridad (state inválido o código ausente).");
			}
			using HttpClient http = new HttpClient();
			Dictionary<string, string> nameValueCollection = new Dictionary<string, string>
			{
				["client_id"] = clientId,
				["grant_type"] = "authorization_code",
				["code"] = code,
				["redirect_uri"] = "http://127.0.0.1:5385/callback/",
				["code_verifier"] = codeVerifier
			};
			HttpResponseMessage tokenResp = await http.PostAsync(tokenEndpoint, new FormUrlEncodedContent(nameValueCollection), linkedCts.Token).ConfigureAwait(continueOnCapturedContext: false);
			string text = await tokenResp.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(continueOnCapturedContext: false);
			using JsonDocument tokenDoc = JsonDocument.Parse(text);
			if (!tokenResp.IsSuccessStatusCode || !tokenDoc.RootElement.TryGetProperty("access_token", out var value2))
			{
				return new AuthResult(Success: false, "Error al canjear token con " + provider + ": " + text);
			}
			string parameter = value2.GetString();
			using HttpRequestMessage userReq = new HttpRequestMessage(HttpMethod.Get, userInfoEndpoint);
			userReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", parameter);
			using JsonDocument userDoc = JsonDocument.Parse(await (await http.SendAsync(userReq, linkedCts.Token).ConfigureAwait(continueOnCapturedContext: false)).Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(continueOnCapturedContext: false));
			string text2 = null;
			string text3 = null;
			string suggestedName = null;
			if (provider == "google")
			{
				text2 = (userDoc.RootElement.TryGetProperty("sub", out var value3) ? value3.GetString() : null);
				text3 = (userDoc.RootElement.TryGetProperty("email", out var value4) ? value4.GetString() : null);
				suggestedName = (userDoc.RootElement.TryGetProperty("name", out var value5) ? value5.GetString() : null);
			}
			else if (provider == "microsoft")
			{
				text2 = (userDoc.RootElement.TryGetProperty("id", out var value6) ? value6.GetString() : null);
				if (userDoc.RootElement.TryGetProperty("mail", out var value7))
				{
					text3 = value7.GetString();
				}
				else
				{
					text3 = (userDoc.RootElement.TryGetProperty("userPrincipalName", out var value8) ? value8.GetString() : null);
				}
				suggestedName = (userDoc.RootElement.TryGetProperty("displayName", out var value9) ? value9.GetString() : null);
			}
			if (string.IsNullOrEmpty(text2))
			{
				return new AuthResult(Success: false, "No se pudo obtener el identificador de usuario desde " + provider + ".");
			}
			return await AuthDb.RegisterOrLoginOAuthAsync(provider, text2, text3 ?? "", suggestedName).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			try
			{
				listener.Stop();
			}
			catch
			{
			}
		}
	}

	private static async Task<AuthResult> PromptOrSimulateDevAuthAsync(string provider)
	{
		await Task.Yield();
		string providerId = "local_" + provider + "_" + Environment.UserName.ToLowerInvariant();
		string email = Environment.UserName.ToLowerInvariant() + "@" + provider + ".com";
		return await AuthDb.RegisterOrLoginOAuthAsync(provider, providerId, email, Environment.UserName).ConfigureAwait(continueOnCapturedContext: false);
	}

	private static async Task SendBrowserResponseAsync(HttpListenerResponse response, bool success, string provider)
	{
		string value = (success ? "#4ade80" : "#ef4444");
		string value2 = (success ? "¡Autenticación Exitosa!" : "Autenticación Cancelada");
		string value3 = (success ? ("Tu cuenta de " + provider.ToUpperInvariant() + " ha sido verificada. Puedes cerrar esta pestaña y volver al launcher.") : ("No se pudo completar el acceso con " + provider + ". Inténtalo nuevamente desde el launcher."));
		string s = $"<!doctype html>\n<html lang=\"es\">\n<head>\n  <meta charset=\"utf-8\">\n  <title>KonsorRO - {value2}</title>\n  <style>\n    body {{\n      margin: 0; padding: 0;\n      background: #14171c;\n      color: #e2e8f0;\n      font-family: -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, sans-serif;\n      display: flex; align-items: center; justify-content: center; height: 100vh;\n    }}\n    .card {{\n      background: #1b1f26;\n      border: 1px solid #2d3748;\n      border-radius: 12px;\n      padding: 36px 48px;\n      text-align: center;\n      box-shadow: 0 10px 25px rgba(0,0,0,0.5);\n      max-width: 420px;\n    }}\n    h1 {{ color: {value}; font-size: 22px; margin: 0 0 12px 0; }}\n    p {{ font-size: 14px; line-height: 1.6; color: #94a3b8; margin: 0; }}\n    .sub {{ margin-top: 20px; font-size: 12px; color: #64748b; }}\n  </style>\n</head>\n<body>\n  <div class=\"card\">\n    <h1>{value2}</h1>\n    <p>{value3}</p>\n    <p class=\"sub\">Esta ventana se puede cerrar.</p>\n  </div>\n  <script>setTimeout(function(){{ window.close(); }}, 4000);</script>\n</body>\n</html>";
		byte[] bytes = Encoding.UTF8.GetBytes(s);
		response.ContentType = "text/html; charset=utf-8";
		response.ContentLength64 = bytes.Length;
		await response.OutputStream.WriteAsync(bytes).ConfigureAwait(continueOnCapturedContext: false);
		response.OutputStream.Close();
	}

	private static string GenerateCodeVerifier()
	{
		return Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
	}

	private static string GenerateCodeChallenge(string verifier)
	{
		return Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
	}

	private static string Base64UrlEncode(byte[] bytes)
	{
		return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_")
			.TrimEnd('=');
	}
}
