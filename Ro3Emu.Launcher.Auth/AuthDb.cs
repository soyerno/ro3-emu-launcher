using System;
using Ro3Emu.Launcher;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Ro3Emu.Launcher.Auth;

public static class AuthDb
{
	private const int SaltBytes = 16;

	private const int HashBytes = 32;

	private const int Iterations = 100000;

	private const int VerificationMinutes = 15;

	private const int MaxFailedAttempts = 5;

	private const int LockoutMinutes = 15;

	private static readonly TimeSpan ResetWindow = TimeSpan.FromMinutes(15L);

	private static string? _customPath;

	public static string DatabasePath => _customPath ?? Path.Combine(AppContext.BaseDirectory, "data", "auth.db");

	private static string ConnectionString => "Data Source=" + DatabasePath;

	public static void ConfigurePath(string path)
	{
		_customPath = path;
	}

	public static async Task InitializeAsync()
	{
		string directoryName = Path.GetDirectoryName(DatabasePath);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		SqliteConnection conn = new SqliteConnection(ConnectionString);
		try
		{
			await ((DbConnection)(object)conn).OpenAsync().ConfigureAwait(continueOnCapturedContext: false);
			SqliteCommand wal = conn.CreateCommand();
			try
			{
				((DbCommand)(object)wal).CommandText = "PRAGMA journal_mode=WAL;";
				await ((DbCommand)(object)wal).ExecuteNonQueryAsync().ConfigureAwait(continueOnCapturedContext: false);
				SqliteCommand cmd = conn.CreateCommand();
				try
				{
					((DbCommand)(object)cmd).CommandText = "CREATE TABLE IF NOT EXISTS accounts (\n    id              INTEGER PRIMARY KEY AUTOINCREMENT,\n    username        TEXT NOT NULL COLLATE NOCASE UNIQUE,\n    password_hash   TEXT,\n    salt            TEXT,\n    auth_provider   TEXT NOT NULL DEFAULT 'local',\n    provider_id     TEXT UNIQUE,\n    email           TEXT,\n    email_verified  INTEGER NOT NULL DEFAULT 0,\n    created_at      TEXT NOT NULL,\n    last_login_at   TEXT,\n    failed_attempts INTEGER NOT NULL DEFAULT 0,\n    locked_until    TEXT,\n    is_gm           INTEGER NOT NULL DEFAULT 0\n);\n\nCREATE INDEX IF NOT EXISTS idx_accounts_username ON accounts(username);\nCREATE INDEX IF NOT EXISTS idx_accounts_provider ON accounts(auth_provider, provider_id);\nCREATE INDEX IF NOT EXISTS idx_accounts_email ON accounts(email COLLATE NOCASE);\n\nCREATE TABLE IF NOT EXISTS email_tokens (\n    id              INTEGER PRIMARY KEY AUTOINCREMENT,\n    account_id      INTEGER NOT NULL,\n    token_hash      TEXT NOT NULL,\n    purpose         TEXT NOT NULL,\n    expires_at      TEXT NOT NULL,\n    used_at         TEXT,\n    created_at      TEXT NOT NULL,\n    FOREIGN KEY(account_id) REFERENCES accounts(id) ON DELETE CASCADE\n);\nCREATE INDEX IF NOT EXISTS idx_email_tokens_lookup ON email_tokens(account_id, purpose, expires_at);\n\nCREATE TABLE IF NOT EXISTS auth_log (\n    id                  INTEGER PRIMARY KEY AUTOINCREMENT,\n    account_id          INTEGER,\n    attempted_username  TEXT NOT NULL,\n    provider            TEXT NOT NULL,\n    success             INTEGER NOT NULL,\n    reason              TEXT,\n    attempted_at        TEXT NOT NULL\n);";
					await ((DbCommand)(object)cmd).ExecuteNonQueryAsync().ConfigureAwait(continueOnCapturedContext: false);
					string[] array = new string[3] { "ALTER TABLE accounts ADD COLUMN email_verified INTEGER NOT NULL DEFAULT 0;", "ALTER TABLE accounts ADD COLUMN failed_attempts INTEGER NOT NULL DEFAULT 0;", "ALTER TABLE accounts ADD COLUMN locked_until TEXT;" };
					Unsafe.SkipInit(out object obj2);
					foreach (string commandText in array)
					{
						try
						{
							SqliteCommand migration = conn.CreateCommand();
							try
							{
								((DbCommand)(object)migration).CommandText = commandText;
								await ((DbCommand)(object)migration).ExecuteNonQueryAsync().ConfigureAwait(continueOnCapturedContext: false);
							}
							finally
							{
								if (migration != null)
								{
									await ((DbCommand)(object)migration).DisposeAsync();
								}
							}
						}
						catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
						{
						}
					}
				}
				finally
				{
					if (cmd != null)
					{
						await ((DbCommand)(object)cmd).DisposeAsync();
					}
				}
			}
			finally
			{
				if (wal != null)
				{
					await ((DbCommand)(object)wal).DisposeAsync();
				}
			}
		}
		finally
		{
			if (conn != null)
			{
				await ((DbConnection)(object)conn).DisposeAsync();
			}
		}
	}

	public static async Task<AuthResult> RegisterLocalAsync(string username, string password, string? email = null, string? confirmPassword = null)
	{
		await InitializeAsync().ConfigureAwait(continueOnCapturedContext: false);
		username = (username ?? "").Trim();
		if (username.Length < 3 || username.Length > 20)
		{
			return new AuthResult(Success: false, Strings.Get("db.user.length"));
		}
		if (!Regex.IsMatch(username, "^[a-zA-Z0-9_\\-]+$"))
		{
			return new AuthResult(Success: false, Strings.Get("db.user.chars"));
		}
		if (string.IsNullOrEmpty(confirmPassword) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(confirmPassword)))
		{
			return new AuthResult(Success: false, Strings.Get("db.pass.mismatch"));
		}
		(bool, string) tuple = ValidateEmail(email);
		if (!tuple.Item1)
		{
			return new AuthResult(Success: false, tuple.Item2);
		}
		if (!ValidatePassword(password, out string error))
		{
			return new AuthResult(Success: false, error);
		}
		byte[] bytes = RandomNumberGenerator.GetBytes(16);
		byte[] inArray = Rfc2898DeriveBytes.Pbkdf2(password, bytes, 100000, HashAlgorithmName.SHA256, 32);
		string saltHex = Convert.ToHexString(bytes);
		string hashHex = Convert.ToHexString(inArray);
		string now = DateTime.UtcNow.ToString("O");
		SqliteConnection conn = new SqliteConnection(ConnectionString);
		AuthResult result;
		try
		{
			await ((DbConnection)(object)conn).OpenAsync().ConfigureAwait(continueOnCapturedContext: false);
			SqliteCommand cmd = conn.CreateCommand();
			AuthResult authResult;
			try
			{
				((DbCommand)(object)cmd).CommandText = "INSERT INTO accounts (username, password_hash, salt, auth_provider, email, email_verified, created_at, last_login_at)\nVALUES (@u, @p, @s, 'local', @e, 0, @c, @c);";
				cmd.Parameters.AddWithValue("@u", (object)username);
				cmd.Parameters.AddWithValue("@p", (object)hashHex);
				cmd.Parameters.AddWithValue("@s", (object)saltHex);
				cmd.Parameters.AddWithValue("@e", ((object)email) ?? ((object)DBNull.Value));
				cmd.Parameters.AddWithValue("@c", (object)now);
				try
				{
					await ((DbCommand)(object)cmd).ExecuteNonQueryAsync().ConfigureAwait(continueOnCapturedContext: false);
					await LogAttemptAsync(conn, null, username, "local", success: true, "REGISTER_OK", now).ConfigureAwait(continueOnCapturedContext: false);
					authResult = new AuthResult(Success: true, Strings.Get("db.registered", username), username, email, "local");
				}
				catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
				{
					await LogAttemptAsync(conn, null, username, "local", success: false, "USERNAME_TAKEN", now).ConfigureAwait(continueOnCapturedContext: false);
					authResult = new AuthResult(Success: false, Strings.Get("db.user.taken"));
				}
				catch (Exception ex)
				{
					authResult = new AuthResult(Success: false, Strings.Get("db.register.error", ex.Message));
				}
			}
			finally
			{
				if (cmd != null)
				{
					await ((DbCommand)(object)cmd).DisposeAsync();
				}
			}
			result = authResult;
		}
		finally
		{
			if (conn != null)
			{
				await ((DbConnection)(object)conn).DisposeAsync();
			}
		}
		return result;
	}

	public static async Task<AuthResult> RequestEmailVerificationAsync(string username)
	{
		await InitializeAsync().ConfigureAwait(continueOnCapturedContext: false);
		SqliteConnection conn = new SqliteConnection(ConnectionString);
		AuthResult result;
		try
		{
			await ((DbConnection)(object)conn).OpenAsync().ConfigureAwait(continueOnCapturedContext: false);
			SqliteCommand cmd = conn.CreateCommand();
			AuthResult authResult2;
			try
			{
				((DbCommand)(object)cmd).CommandText = "SELECT id, email, email_verified FROM accounts WHERE username = @u COLLATE NOCASE LIMIT 1;";
				cmd.Parameters.AddWithValue("@u", (object)username.Trim());
				SqliteDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(continueOnCapturedContext: false);
				AuthResult authResult;
				try
				{
					if (!(await ((DbDataReader)(object)reader).ReadAsync().ConfigureAwait(continueOnCapturedContext: false)))
					{
						authResult = new AuthResult(Success: false, "Usuario no encontrado.");
					}
					else
					{
						long @int = ((DbDataReader)(object)reader).GetInt64(0);
						string text = (((DbDataReader)(object)reader).IsDBNull(1) ? "" : ((DbDataReader)(object)reader).GetString(1));
						bool flag = ((DbDataReader)(object)reader).GetInt64(2) != 0;
						((DbDataReader)(object)reader).Close();
						if (flag)
						{
							authResult = new AuthResult(Success: true, Strings.Get("db.email.verified"), username, text, "local");
						}
						else
						{
							authResult = ((!string.IsNullOrWhiteSpace(text)) ? (await CreateAndSendTokenAsync(conn, @int, username, text, "verify_email").ConfigureAwait(continueOnCapturedContext: false)) : new AuthResult(Success: false, Strings.Get("db.email.none")));
						}
					}
				}
				finally
				{
					if (reader != null)
					{
						await ((DbDataReader)(object)reader).DisposeAsync();
					}
				}
				authResult2 = authResult;
			}
			finally
			{
				if (cmd != null)
				{
					await ((DbCommand)(object)cmd).DisposeAsync();
				}
			}
			result = authResult2;
		}
		finally
		{
			if (conn != null)
			{
				await ((DbConnection)(object)conn).DisposeAsync();
			}
		}
		return result;
	}

	public static async Task<AuthResult> VerifyEmailAsync(string username, string code)
	{
		return await ConsumeTokenAsync(username, code, "verify_email", activate: true).ConfigureAwait(continueOnCapturedContext: false);
	}

	public static async Task<AuthResult> RequestPasswordResetAsync(string email)
	{
		(bool, string) tuple = ValidateEmail(email);
		if (!tuple.Item1)
		{
			return new AuthResult(Success: false, tuple.Item2);
		}
		await InitializeAsync().ConfigureAwait(continueOnCapturedContext: false);
		SqliteConnection conn = new SqliteConnection(ConnectionString);
		AuthResult result;
		try
		{
			await ((DbConnection)(object)conn).OpenAsync().ConfigureAwait(continueOnCapturedContext: false);
			SqliteCommand cmd = conn.CreateCommand();
			AuthResult authResult2;
			try
			{
				((DbCommand)(object)cmd).CommandText = "SELECT id, username, email FROM accounts WHERE email = @e COLLATE NOCASE AND auth_provider = 'local' LIMIT 1;";
				cmd.Parameters.AddWithValue("@e", (object)email.Trim());
				SqliteDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(continueOnCapturedContext: false);
				AuthResult authResult;
				try
				{
					if (!(await ((DbDataReader)(object)reader).ReadAsync().ConfigureAwait(continueOnCapturedContext: false)))
					{
						authResult = new AuthResult(Success: false, "No existe una cuenta local con ese correo.");
					}
					else
					{
						long @int = ((DbDataReader)(object)reader).GetInt64(0);
						string username = ((DbDataReader)(object)reader).GetString(1);
						string email2 = ((DbDataReader)(object)reader).GetString(2);
						((DbDataReader)(object)reader).Close();
						authResult = await CreateAndSendTokenAsync(conn, @int, username, email2, "reset_password").ConfigureAwait(continueOnCapturedContext: false);
					}
				}
				finally
				{
					if (reader != null)
					{
						await ((DbDataReader)(object)reader).DisposeAsync();
					}
				}
				authResult2 = authResult;
			}
			finally
			{
				if (cmd != null)
				{
					await ((DbCommand)(object)cmd).DisposeAsync();
				}
			}
			result = authResult2;
		}
		finally
		{
			if (conn != null)
			{
				await ((DbConnection)(object)conn).DisposeAsync();
			}
		}
		return result;
	}

	public static async Task<AuthResult> ResetPasswordAsync(string username, string code, string newPassword, string confirmPassword)
	{
		if (!ValidatePassword(newPassword, out string error))
		{
			return new AuthResult(Success: false, error);
		}
		if (newPassword != confirmPassword)
		{
			return new AuthResult(Success: false, Strings.Get("db.pass.mismatch"));
		}
		AuthResult authResult = await ConsumeTokenAsync(username, code, "reset_password", activate: false).ConfigureAwait(continueOnCapturedContext: false);
		if (!authResult.Success)
		{
			return authResult;
		}
		byte[] salt = RandomNumberGenerator.GetBytes(16);
		byte[] hash = Rfc2898DeriveBytes.Pbkdf2(newPassword, salt, 100000, HashAlgorithmName.SHA256, 32);
		SqliteConnection conn = new SqliteConnection(ConnectionString);
		AuthResult result;
		try
		{
			await ((DbConnection)(object)conn).OpenAsync().ConfigureAwait(continueOnCapturedContext: false);
			SqliteCommand cmd = conn.CreateCommand();
			AuthResult authResult2;
			try
			{
				((DbCommand)(object)cmd).CommandText = "UPDATE accounts SET password_hash=@p, salt=@s, failed_attempts=0, locked_until=NULL WHERE username=@u COLLATE NOCASE AND auth_provider='local';";
				cmd.Parameters.AddWithValue("@p", (object)Convert.ToHexString(hash));
				cmd.Parameters.AddWithValue("@s", (object)Convert.ToHexString(salt));
				cmd.Parameters.AddWithValue("@u", (object)username.Trim());
				await ((DbCommand)(object)cmd).ExecuteNonQueryAsync().ConfigureAwait(continueOnCapturedContext: false);
				authResult2 = new AuthResult(Success: true, Strings.Get("db.pass.reset"), username, null, "local");
			}
			finally
			{
				if (cmd != null)
				{
					await ((DbCommand)(object)cmd).DisposeAsync();
				}
			}
			result = authResult2;
		}
		finally
		{
			if (conn != null)
			{
				await ((DbConnection)(object)conn).DisposeAsync();
			}
		}
		return result;
	}

	public static async Task<AuthResult> VerifyLocalAsync(string username, string password)
	{
		await InitializeAsync().ConfigureAwait(continueOnCapturedContext: false);
		username = (username ?? "").Trim();
		string now = DateTime.UtcNow.ToString("O");
		SqliteConnection conn = new SqliteConnection(ConnectionString);
		AuthResult result;
		try
		{
			await ((DbConnection)(object)conn).OpenAsync().ConfigureAwait(continueOnCapturedContext: false);
			SqliteCommand cmd = conn.CreateCommand();
			AuthResult authResult3;
			try
			{
				((DbCommand)(object)cmd).CommandText = "SELECT id, username, password_hash, salt, email, email_verified, failed_attempts, locked_until FROM accounts WHERE (username = @u COLLATE NOCASE OR email = @u COLLATE NOCASE) AND auth_provider = 'local' LIMIT 1;";
				cmd.Parameters.AddWithValue("@u", (object)username);
				SqliteDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(continueOnCapturedContext: false);
				AuthResult authResult;
				try
				{
					long id;
					string realUsername;
					string s;
					string s2;
					string email;
					bool flag;
					int num;
					if (!(await ((DbDataReader)(object)reader).ReadAsync().ConfigureAwait(continueOnCapturedContext: false)))
					{
						await LogAttemptAsync(conn, null, username, "local", success: false, "USER_NOT_FOUND", now).ConfigureAwait(continueOnCapturedContext: false);
						authResult = new AuthResult(Success: false, "Usuario no encontrado.");
					}
					else
					{
						id = ((DbDataReader)(object)reader).GetInt64(0);
						realUsername = ((DbDataReader)(object)reader).GetString(1);
						s = ((DbDataReader)(object)reader).GetString(2);
						s2 = ((DbDataReader)(object)reader).GetString(3);
						email = (((DbDataReader)(object)reader).IsDBNull(4) ? null : ((DbDataReader)(object)reader).GetString(4));
						flag = !((DbDataReader)(object)reader).IsDBNull(5) && ((DbDataReader)(object)reader).GetInt64(5) != 0;
						num = ((!((DbDataReader)(object)reader).IsDBNull(6)) ? ((DbDataReader)(object)reader).GetInt32(6) : 0);
						DateTime? dateTime = (((DbDataReader)(object)reader).IsDBNull(7) ? ((DateTime?)null) : new DateTime?(DateTime.Parse(((DbDataReader)(object)reader).GetString(7))));
						if (!dateTime.HasValue)
						{
							goto IL_04f4;
						}
						DateTime valueOrDefault = dateTime.GetValueOrDefault();
						if (!(valueOrDefault > DateTime.UtcNow))
						{
							goto IL_04f4;
						}
						authResult = new AuthResult(Success: false, Strings.Get("db.locked"));
					}
					goto end_IL_02cd;
					IL_04f4:
					if (!flag)
					{
						authResult = new AuthResult(Success: false, Strings.Get("db.verify.first"), realUsername, email, "local");
					}
					else
					{
						byte[] salt = Convert.FromHexString(s2);
						byte[] array = Convert.FromHexString(s);
						AuthResult authResult2;
						if (!CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(password, salt, 100000, HashAlgorithmName.SHA256, 32), array))
						{
							int nextFailures = num + 1;
							SqliteCommand failCmd = conn.CreateCommand();
							try
							{
								((DbCommand)(object)failCmd).CommandText = ((nextFailures >= 5) ? "UPDATE accounts SET failed_attempts=0, locked_until=@until WHERE id=@id;" : "UPDATE accounts SET failed_attempts=failed_attempts+1 WHERE id=@id;");
								failCmd.Parameters.AddWithValue("@id", (object)id);
								if (nextFailures >= 5)
								{
									failCmd.Parameters.AddWithValue("@until", (object)DateTime.UtcNow.AddMinutes(15.0).ToString("O"));
								}
								await ((DbCommand)(object)failCmd).ExecuteNonQueryAsync().ConfigureAwait(continueOnCapturedContext: false);
								await LogAttemptAsync(conn, id, username, "local", success: false, "BAD_PASSWORD", now).ConfigureAwait(continueOnCapturedContext: false);
								authResult2 = new AuthResult(Success: false, (nextFailures >= 5) ? Strings.Get("db.locked.now") : Strings.Get("db.pass.wrong"));
							}
							finally
							{
								if (failCmd != null)
								{
									await ((DbCommand)(object)failCmd).DisposeAsync();
								}
							}
							authResult = authResult2;
						}
						else
						{
							((DbDataReader)(object)reader).Close();
							SqliteCommand updateCmd = conn.CreateCommand();
							try
							{
								((DbCommand)(object)updateCmd).CommandText = "UPDATE accounts SET last_login_at = @now WHERE id = @id;";
								updateCmd.Parameters.AddWithValue("@now", (object)now);
								updateCmd.Parameters.AddWithValue("@id", (object)id);
								await ((DbCommand)(object)updateCmd).ExecuteNonQueryAsync().ConfigureAwait(continueOnCapturedContext: false);
								await LogAttemptAsync(conn, id, realUsername, "local", success: true, "LOGIN_OK", now).ConfigureAwait(continueOnCapturedContext: false);
								authResult2 = new AuthResult(Success: true, Strings.Get("db.signed.in"), realUsername, email, "local");
							}
							finally
							{
								if (updateCmd != null)
								{
									await ((DbCommand)(object)updateCmd).DisposeAsync();
								}
							}
							authResult = authResult2;
						}
					}
					end_IL_02cd:;
				}
				finally
				{
					if (reader != null)
					{
						await ((DbDataReader)(object)reader).DisposeAsync();
					}
				}
				authResult3 = authResult;
			}
			finally
			{
				if (cmd != null)
				{
					await ((DbCommand)(object)cmd).DisposeAsync();
				}
			}
			result = authResult3;
		}
		finally
		{
			if (conn != null)
			{
				await ((DbConnection)(object)conn).DisposeAsync();
			}
		}
		return result;
	}

	public static async Task<AuthResult> RegisterOrLoginOAuthAsync(string provider, string providerId, string email, string? suggestedName = null)
	{
		await InitializeAsync().ConfigureAwait(continueOnCapturedContext: false);
		string now = DateTime.UtcNow.ToString("O");
		provider = provider.ToLowerInvariant();
		SqliteConnection conn = new SqliteConnection(ConnectionString);
		AuthResult result;
		try
		{
			await ((DbConnection)(object)conn).OpenAsync().ConfigureAwait(continueOnCapturedContext: false);
			SqliteCommand checkCmd = conn.CreateCommand();
			AuthResult authResult3;
			try
			{
				((DbCommand)(object)checkCmd).CommandText = "SELECT id, username, email FROM accounts WHERE auth_provider = @p AND provider_id = @pid LIMIT 1;";
				checkCmd.Parameters.AddWithValue("@p", (object)provider);
				checkCmd.Parameters.AddWithValue("@pid", (object)providerId);
				SqliteDataReader reader = await checkCmd.ExecuteReaderAsync().ConfigureAwait(continueOnCapturedContext: false);
				AuthResult authResult2;
				try
				{
					if (await ((DbDataReader)(object)reader).ReadAsync().ConfigureAwait(continueOnCapturedContext: false))
					{
						long existingId = ((DbDataReader)(object)reader).GetInt64(0);
						string existingUsername = ((DbDataReader)(object)reader).GetString(1);
						string existingEmail = (((DbDataReader)(object)reader).IsDBNull(2) ? email : ((DbDataReader)(object)reader).GetString(2));
						((DbDataReader)(object)reader).Close();
						SqliteCommand updateCmd = conn.CreateCommand();
						AuthResult authResult;
						try
						{
							((DbCommand)(object)updateCmd).CommandText = "UPDATE accounts SET last_login_at = @now, email = @e WHERE id = @id;";
							updateCmd.Parameters.AddWithValue("@now", (object)now);
							updateCmd.Parameters.AddWithValue("@e", ((object)email) ?? ((object)DBNull.Value));
							updateCmd.Parameters.AddWithValue("@id", (object)existingId);
							await ((DbCommand)(object)updateCmd).ExecuteNonQueryAsync().ConfigureAwait(continueOnCapturedContext: false);
							await LogAttemptAsync(conn, existingId, existingUsername, provider, success: true, "OAUTH_LOGIN_OK", now).ConfigureAwait(continueOnCapturedContext: false);
							authResult = new AuthResult(Success: true, Strings.Get("db.welcome.back", existingUsername), existingUsername, existingEmail, provider);
						}
						finally
						{
							if (updateCmd != null)
							{
								await ((DbCommand)(object)updateCmd).DisposeAsync();
							}
						}
						authResult2 = authResult;
						goto IL_06ec;
					}
				}
				finally
				{
					if (reader != null)
					{
						await ((DbDataReader)(object)reader).DisposeAsync();
					}
				}
				goto end_IL_01d4;
				IL_06ec:
				authResult3 = authResult2;
				goto IL_07c5;
				end_IL_01d4:;
			}
			finally
			{
				if (checkCmd != null)
				{
					await ((DbCommand)(object)checkCmd).DisposeAsync();
				}
			}
			authResult3 = null;
			string text = CleanUsername(suggestedName);
			if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(email))
			{
				text = CleanUsername(email.Split('@')[0]);
			}
			if (string.IsNullOrEmpty(text))
			{
				text = provider + "_player";
			}
			string finalUsername = await FindAvailableUsernameAsync(conn, text).ConfigureAwait(continueOnCapturedContext: false);
			SqliteCommand insertCmd = conn.CreateCommand();
			try
			{
				((DbCommand)(object)insertCmd).CommandText = "INSERT INTO accounts (username, auth_provider, provider_id, email, created_at, last_login_at)\nVALUES (@u, @p, @pid, @e, @now, @now);";
				insertCmd.Parameters.AddWithValue("@u", (object)finalUsername);
				insertCmd.Parameters.AddWithValue("@p", (object)provider);
				insertCmd.Parameters.AddWithValue("@pid", (object)providerId);
				insertCmd.Parameters.AddWithValue("@e", ((object)email) ?? ((object)DBNull.Value));
				insertCmd.Parameters.AddWithValue("@now", (object)now);
				await ((DbCommand)(object)insertCmd).ExecuteNonQueryAsync().ConfigureAwait(continueOnCapturedContext: false);
				await LogAttemptAsync(conn, null, finalUsername, provider, success: true, "OAUTH_REGISTER_OK", now).ConfigureAwait(continueOnCapturedContext: false);
				authResult3 = new AuthResult(Success: true, Strings.Get("db.oauth.created", provider, finalUsername), finalUsername, email, provider);
			}
			finally
			{
				if (insertCmd != null)
				{
					await ((DbCommand)(object)insertCmd).DisposeAsync();
				}
			}
			result = authResult3;
			goto end_IL_012a;
			IL_07c5:
			result = authResult3;
			end_IL_012a:;
		}
		finally
		{
			if (conn != null)
			{
				await ((DbConnection)(object)conn).DisposeAsync();
			}
		}
		return result;
	}

	public static async Task<string?> GetLastAccountAsync()
	{
		_ = 3;
		try
		{
			if (!File.Exists(DatabasePath))
			{
				return null;
			}
			SqliteConnection conn = new SqliteConnection(ConnectionString);
			string result;
			try
			{
				await ((DbConnection)(object)conn).OpenAsync().ConfigureAwait(continueOnCapturedContext: false);
				SqliteCommand cmd = conn.CreateCommand();
				string text;
				try
				{
					((DbCommand)(object)cmd).CommandText = "SELECT username FROM accounts ORDER BY last_login_at DESC LIMIT 1;";
					text = (await ((DbCommand)(object)cmd).ExecuteScalarAsync().ConfigureAwait(continueOnCapturedContext: false))?.ToString();
				}
				finally
				{
					if (cmd != null)
					{
						await ((DbCommand)(object)cmd).DisposeAsync();
					}
				}
				result = text;
			}
			finally
			{
				if (conn != null)
				{
					await ((DbConnection)(object)conn).DisposeAsync();
				}
			}
			return result;
		}
		catch
		{
			return null;
		}
	}

	public static async Task RecordLoginAsync(string username)
	{
		_ = 3;
		try
		{
			SqliteConnection conn = new SqliteConnection(ConnectionString);
			try
			{
				await ((DbConnection)(object)conn).OpenAsync().ConfigureAwait(continueOnCapturedContext: false);
				SqliteCommand cmd = conn.CreateCommand();
				try
				{
					((DbCommand)(object)cmd).CommandText = "INSERT INTO accounts (username, auth_provider, created_at, last_login_at, email_verified)\nVALUES (@u, 'server', @now, @now, 1)\nON CONFLICT(username) DO UPDATE SET last_login_at = excluded.last_login_at;";
					cmd.Parameters.AddWithValue("@u", (object)username);
					cmd.Parameters.AddWithValue("@now", (object)DateTime.UtcNow.ToString("O"));
					await ((DbCommand)(object)cmd).ExecuteNonQueryAsync().ConfigureAwait(continueOnCapturedContext: false);
				}
				finally
				{
					if (cmd != null)
					{
						await ((DbCommand)(object)cmd).DisposeAsync();
					}
				}
			}
			finally
			{
				if (conn != null)
				{
					await ((DbConnection)(object)conn).DisposeAsync();
				}
			}
		}
		catch
		{
		}
	}

	private static async Task<AuthResult> CreateAndSendTokenAsync(SqliteConnection conn, long accountId, string username, string email, string purpose)
	{
		string code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
		string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
		string now = DateTime.UtcNow.ToString("O");
		string expires = DateTime.UtcNow.AddMinutes(15.0).ToString("O");
		SqliteCommand delete = conn.CreateCommand();
		AuthResult result;
		try
		{
			((DbCommand)(object)delete).CommandText = "DELETE FROM email_tokens WHERE account_id=@id AND purpose=@purpose AND used_at IS NULL;";
			delete.Parameters.AddWithValue("@id", (object)accountId);
			delete.Parameters.AddWithValue("@purpose", (object)purpose);
			await ((DbCommand)(object)delete).ExecuteNonQueryAsync().ConfigureAwait(continueOnCapturedContext: false);
			SqliteCommand insert = conn.CreateCommand();
			AuthResult authResult;
			try
			{
				((DbCommand)(object)insert).CommandText = "INSERT INTO email_tokens(account_id,token_hash,purpose,expires_at,created_at) VALUES(@id,@hash,@purpose,@expires,@now);";
				insert.Parameters.AddWithValue("@id", (object)accountId);
				insert.Parameters.AddWithValue("@hash", (object)hash);
				insert.Parameters.AddWithValue("@purpose", (object)purpose);
				insert.Parameters.AddWithValue("@expires", (object)expires);
				insert.Parameters.AddWithValue("@now", (object)now);
				await ((DbCommand)(object)insert).ExecuteNonQueryAsync().ConfigureAwait(continueOnCapturedContext: false);
				(bool, string) tuple = ((!(purpose == "verify_email")) ? (await EmailService.SendPasswordResetEmailAsync(email, username, code).ConfigureAwait(continueOnCapturedContext: false)) : (await EmailService.SendVerificationEmailAsync(email, username, code).ConfigureAwait(continueOnCapturedContext: false)));
				(bool, string) tuple2 = tuple;
				authResult = new AuthResult(tuple2.Item1, tuple2.Item2, username, email, "local");
			}
			finally
			{
				if (insert != null)
				{
					await ((DbCommand)(object)insert).DisposeAsync();
				}
			}
			result = authResult;
		}
		finally
		{
			if (delete != null)
			{
				await ((DbCommand)(object)delete).DisposeAsync();
			}
		}
		return result;
	}

	private static async Task<AuthResult> ConsumeTokenAsync(string username, string code, string purpose, bool activate)
	{
		await InitializeAsync().ConfigureAwait(continueOnCapturedContext: false);
		string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim())));
		SqliteConnection conn = new SqliteConnection(ConnectionString);
		AuthResult result;
		try
		{
			await ((DbConnection)(object)conn).OpenAsync().ConfigureAwait(continueOnCapturedContext: false);
			SqliteCommand cmd = conn.CreateCommand();
			AuthResult authResult4;
			try
			{
				((DbCommand)(object)cmd).CommandText = "SELECT t.id,t.account_id,a.email FROM email_tokens t JOIN accounts a ON a.id=t.account_id WHERE a.username=@u COLLATE NOCASE AND t.purpose=@purpose AND t.token_hash=@hash AND t.used_at IS NULL AND t.expires_at > @now ORDER BY t.id DESC LIMIT 1;";
				cmd.Parameters.AddWithValue("@u", (object)username.Trim());
				cmd.Parameters.AddWithValue("@purpose", (object)purpose);
				cmd.Parameters.AddWithValue("@hash", (object)hash);
				cmd.Parameters.AddWithValue("@now", (object)DateTime.UtcNow.ToString("O"));
				SqliteDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(continueOnCapturedContext: false);
				AuthResult authResult;
				try
				{
					if (!(await ((DbDataReader)(object)reader).ReadAsync().ConfigureAwait(continueOnCapturedContext: false)))
					{
						authResult = new AuthResult(Success: false, Strings.Get("db.code.invalid"));
					}
					else
					{
						long tokenId = ((DbDataReader)(object)reader).GetInt64(0);
						long accountId = ((DbDataReader)(object)reader).GetInt64(1);
						string email = (((DbDataReader)(object)reader).IsDBNull(2) ? "" : ((DbDataReader)(object)reader).GetString(2));
						((DbDataReader)(object)reader).Close();
						AuthResult authResult3;
						await using (DbTransaction tx = await ((DbConnection)(object)conn).BeginTransactionAsync(default(CancellationToken)).ConfigureAwait(continueOnCapturedContext: false))
						{
							SqliteCommand mark = conn.CreateCommand();
							AuthResult authResult2;
							try
							{
								mark.Transaction = (SqliteTransaction)tx;
								((DbCommand)(object)mark).CommandText = "UPDATE email_tokens SET used_at=@now WHERE id=@id;";
								mark.Parameters.AddWithValue("@now", (object)DateTime.UtcNow.ToString("O"));
								mark.Parameters.AddWithValue("@id", (object)tokenId);
								await ((DbCommand)(object)mark).ExecuteNonQueryAsync().ConfigureAwait(continueOnCapturedContext: false);
								if (activate)
								{
									SqliteCommand activateCmd = conn.CreateCommand();
									try
									{
										activateCmd.Transaction = (SqliteTransaction)tx;
										((DbCommand)(object)activateCmd).CommandText = "UPDATE accounts SET email_verified=1 WHERE id=@id;";
										activateCmd.Parameters.AddWithValue("@id", (object)accountId);
										await ((DbCommand)(object)activateCmd).ExecuteNonQueryAsync().ConfigureAwait(continueOnCapturedContext: false);
									}
									finally
									{
										if (activateCmd != null)
										{
											await ((DbCommand)(object)activateCmd).DisposeAsync();
										}
									}
								}
								await tx.CommitAsync().ConfigureAwait(continueOnCapturedContext: false);
								authResult2 = new AuthResult(Success: true, activate ? Strings.Get("db.code.activated") : Strings.Get("db.code.recovery"), username, email, "local");
							}
							finally
							{
								if (mark != null)
								{
									await ((DbCommand)(object)mark).DisposeAsync();
								}
							}
							authResult3 = authResult2;
						}
						authResult = authResult3;
					}
				}
				finally
				{
					if (reader != null)
					{
						await ((DbDataReader)(object)reader).DisposeAsync();
					}
				}
				authResult4 = authResult;
			}
			finally
			{
				if (cmd != null)
				{
					await ((DbCommand)(object)cmd).DisposeAsync();
				}
			}
			result = authResult4;
		}
		finally
		{
			if (conn != null)
			{
				await ((DbConnection)(object)conn).DisposeAsync();
			}
		}
		return result;
	}

	private static (bool Valid, string Message) ValidateEmail(string? email)
	{
		if (string.IsNullOrWhiteSpace(email))
		{
			return (Valid: false, Message: Strings.Get("db.email.required"));
		}
		try
		{
			if (!string.Equals(new MailAddress(email.Trim()).Address, email.Trim(), StringComparison.OrdinalIgnoreCase))
			{
				return (Valid: false, Message: Strings.Get("db.email.invalid"));
			}
			return (Valid: true, Message: "");
		}
		catch
		{
			return (Valid: false, Message: Strings.Get("db.email.invalid"));
		}
	}

	private static bool ValidatePassword(string password, out string error)
	{
		error = "";
		if (password.Length < 6)
		{
			error = Strings.Get("db.pass.min");
			return false;
		}
		if (password.Count(char.IsDigit) < 2)
		{
			error = Strings.Get("db.pass.numbers");
			return false;
		}
		if (!password.Any((char c) => !char.IsLetterOrDigit(c)))
		{
			error = Strings.Get("db.pass.symbol");
			return false;
		}
		return true;
	}

	private static string CleanUsername(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return "";
		}
		string text = Regex.Replace(raw.Trim(), "[^a-zA-Z0-9_\\-]", "");
		if (text.Length > 16)
		{
			text = text.Substring(0, 16);
		}
		return text;
	}

	private static async Task<string> FindAvailableUsernameAsync(SqliteConnection conn, string baseName)
	{
		string candidate = baseName;
		int suffix = 1;
		while (true)
		{
			SqliteCommand cmd = conn.CreateCommand();
			try
			{
				((DbCommand)(object)cmd).CommandText = "SELECT COUNT(*) FROM accounts WHERE username = @u;";
				cmd.Parameters.AddWithValue("@u", (object)candidate);
				if ((long)((await ((DbCommand)(object)cmd).ExecuteScalarAsync().ConfigureAwait(continueOnCapturedContext: false)) ?? ((object)0)) == 0L)
				{
					return candidate;
				}
				candidate = $"{baseName}_{suffix++}";
				if (candidate.Length > 20)
				{
					candidate = candidate.Substring(0, 20);
				}
			}
			finally
			{
				if (cmd != null)
				{
					await ((DbCommand)(object)cmd).DisposeAsync();
				}
			}
			string text = null;
		}
	}

	private static async Task LogAttemptAsync(SqliteConnection conn, long? accountId, string username, string provider, bool success, string reason, string now)
	{
		_ = 1;
		try
		{
			SqliteCommand cmd = conn.CreateCommand();
			try
			{
				((DbCommand)(object)cmd).CommandText = "INSERT INTO auth_log (account_id, attempted_username, provider, success, reason, attempted_at)\nVALUES (@aid, @u, @p, @s, @r, @now);";
				cmd.Parameters.AddWithValue("@aid", ((object)accountId) ?? DBNull.Value);
				cmd.Parameters.AddWithValue("@u", (object)username);
				cmd.Parameters.AddWithValue("@p", (object)provider);
				cmd.Parameters.AddWithValue("@s", (object)(success ? 1 : 0));
				cmd.Parameters.AddWithValue("@r", (object)reason);
				cmd.Parameters.AddWithValue("@now", (object)now);
				await ((DbCommand)(object)cmd).ExecuteNonQueryAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			finally
			{
				if (cmd != null)
				{
					await ((DbCommand)(object)cmd).DisposeAsync();
				}
			}
		}
		catch
		{
		}
	}
}
