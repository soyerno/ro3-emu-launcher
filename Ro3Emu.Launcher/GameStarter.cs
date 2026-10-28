using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ro3Emu.Launcher;

public static class GameStarter
{
	public static async Task<string?> StartAsync(string clientDir, string? launchCode = null, string? loginAccount = null, string? serverUrl = null)
	{
		string text = Path.Combine(clientDir, "ro3.exe");
		if (!File.Exists(text))
		{
			return "ro3.exe not found";
		}
		using AnonymousPipeServerStream pipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
		using AnonymousPipeServerStream authPipe = ((!string.IsNullOrEmpty(launchCode)) ? new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable) : null);
		ProcessStartInfo processStartInfo = new ProcessStartInfo(text)
		{
			UseShellExecute = false,
			WorkingDirectory = clientDir
		};
		processStartInfo.Environment["RO3_LAUNCH_PIPE_HANDLE"] = pipe.GetClientHandleAsString();
		if (authPipe != null)
		{
			processStartInfo.Environment["RO3_AUTH_PIPE_HANDLE"] = authPipe.GetClientHandleAsString();
			processStartInfo.Environment["RO3EMU_LOGIN_ACCOUNT"] = (string.IsNullOrEmpty(loginAccount) ? "player" : loginAccount);
		}
		if (!string.IsNullOrEmpty(serverUrl))
		{
			processStartInfo.Environment["RO3EMU_SERVER"] = serverUrl.TrimEnd('/');
		}
		using Process game = Process.Start(processStartInfo) ?? throw new InvalidOperationException("ro3.exe did not start");
		pipe.DisposeLocalCopyOfClientHandle();
		authPipe?.DisposeLocalCopyOfClientHandle();
		long num = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		InlineArray5<string> buffer = default;
		buffer[0] = "RO3_LAUNCH_PROOF_V1";
		buffer[1] = game.Id.ToString();
		buffer[2] = num.ToString();
		buffer[3] = (num + 30).ToString();
		buffer[4] = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
		string s = string.Join("\r\n", (ReadOnlySpan<string?>)buffer) + "\r\n";
		byte[] bytes = Encoding.ASCII.GetBytes(s);
		await pipe.WriteAsync(bytes);
		await pipe.FlushAsync();
		if (authPipe != null && !string.IsNullOrEmpty(launchCode))
		{
			string s2 = (loginAccount ?? "player") + "\r\n" + launchCode + "\r\n";
			byte[] bytes2 = Encoding.UTF8.GetBytes(s2);
			await authPipe.WriteAsync(bytes2);
			await authPipe.FlushAsync();
		}
		using CancellationTokenSource wait = new CancellationTokenSource(TimeSpan.FromSeconds(5L));
		_ = 4;
		try
		{
			await game.WaitForExitAsync(wait.Token);
			return (game.ExitCode == 70) ? "the game rejected the launch proof (exit 70)" : $"the game closed right away (exit {game.ExitCode})";
		}
		catch (OperationCanceledException)
		{
			return null;
		}
	}
}
