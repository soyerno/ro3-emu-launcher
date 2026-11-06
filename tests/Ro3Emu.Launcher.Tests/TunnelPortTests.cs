using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Ro3Emu.Launcher;
using Xunit;

namespace Ro3Emu.Launcher.Tests;

public class TunnelPortTests
{
	[Fact]
	public void TheListenerOfAPortIsFound()
	{
		using TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;

		PortOwner? owner = TunnelManager.FindPortOwner(port);

		Assert.NotNull(owner);
		Assert.Equal(Environment.ProcessId, owner.ProcessId);
		Assert.Equal(Process.GetCurrentProcess().ProcessName, owner.ProcessName);
	}

	[Fact]
	public void AFreePortHasNoListener()
	{
		using TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();

		Assert.Null(TunnelManager.FindPortOwner(port));
	}

	[Fact]
	public async Task ATunnelPortHeldByAnotherProgramFailsAndNamesIt()
	{
		// A local test server on the tunnel port used to swallow the game: the launcher reported "connected"
		// and the game reached that server instead of the realm.
		using TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		string dir = Directory.CreateTempSubdirectory("ro3-tunnel-").FullName;
		try
		{
			// Never started: the busy port stops the launch before cloudflared runs.
			File.WriteAllBytes(Path.Combine(dir, "cloudflared.exe"), []);
			using TunnelManager tunnels = new([new TunnelSpec("ro3-http.konsor.example", port)]);
			List<string> statuses = [];

			bool ok = await tunnels.StartTunnelsAsync(dir, CancellationToken.None, statuses.Add);

			Assert.False(ok);
			Assert.False(tunnels.IsConnected);
			string name = Process.GetCurrentProcess().ProcessName;
			Assert.NotNull(tunnels.LastError);
			Assert.Contains(port.ToString(), tunnels.LastError);
			Assert.Contains($"{name} (PID {Environment.ProcessId})", tunnels.LastError);
			Assert.Contains(tunnels.LastError, statuses);
			Assert.True(TunnelManager.TestLocalPort(port), "the other program's listener must be left running");
		}
		finally
		{
			Directory.Delete(dir, true);
		}
	}
}
