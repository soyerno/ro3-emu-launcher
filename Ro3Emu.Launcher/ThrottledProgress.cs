using System;
using System.Diagnostics;
using System.Threading;
using Ro3Emu.Patcher;

namespace Ro3Emu.Launcher;

public sealed class ThrottledProgress(Action<UpdateProgress> show) : IProgress<UpdateProgress>
{
	private readonly Lock _gate = new Lock();
	private readonly Stopwatch _clock = Stopwatch.StartNew();
	private long _lastShown = long.MinValue;
	private UpdateStage? _stage;

	public void Report(UpdateProgress value)
	{
		using (_gate.EnterScope())
		{
			long elapsedMilliseconds = _clock.ElapsedMilliseconds;
			if (_stage == value.Stage && elapsedMilliseconds - _lastShown < 150)
			{
				return;
			}
			_stage = value.Stage;
			_lastShown = elapsedMilliseconds;
		}
		show(value);
	}
}
