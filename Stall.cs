using System.Diagnostics;
using System.Threading;

namespace Berpiztu.KeyPlus;

/// <summary>
/// The screen's thread watched, Windows' and Android's alike, for a key felt
/// slow to be found in the log: a thread of its own asks it to answer every
/// <see cref="CheckMs"/>, and where it takes <see cref="SlowMs"/> or more the
/// log says, always, how long it did not answer, the steps it was in
/// meanwhile as they were seen (each marked with <see cref="Doing"/>), the
/// app's memory and the garbage collections since the line before. A step
/// names what the keyboard does and how long the text is, never the text.
/// </summary>
public static class Stall
{
    private const int CheckMs = 50;
    private const int SlowMs = 200;

    /// <summary>The most steps written for one wait: the first ones, where it began.</summary>
    private const int MostSteps = 8;

    private const string NothingMarked = "nothing marked";

    private static volatile string doing = NothingMarked;
    private static Thread? watching;

    /// <summary>The screen's thread, as its first answer tells: a step marked on any other is not its own and is left out.</summary>
    private static volatile int screenThread = -1;

    /// <summary>The watch started, once: <paramref name="onScreen"/> runs what it is given on the screen's thread.</summary>
    public static void Watch(Action<Action> onScreen)
    {
        if (watching is not null)
        {
            return;
        }

        watching = new Thread(() => Run(onScreen)) { IsBackground = true, Name = "Stall watch" };
        watching.Start();
    }

    /// <summary>What the screen's thread does from now until the step returned is disposed, the one before it marked again; on another thread, nothing.</summary>
    public static Step Doing(string what)
    {
        if (Environment.CurrentManagedThreadId != screenThread)
        {
            return default;
        }

        var before = doing;
        doing = what;
        return new Step(before);
    }

    /// <summary>A step marked: disposed, the one it was in is marked again.</summary>
    public readonly struct Step(string? before) : IDisposable
    {
        public void Dispose()
        {
            if (before is not null)
            {
                doing = before;
            }
        }
    }

    private static void Run(Action<Action> onScreen)
    {
        var collections = Collections();
        while (true)
        {
            var answered = new ManualResetEventSlim(false);
            var asked = Stopwatch.StartNew();
            try
            {
                onScreen(() =>
                {
                    screenThread = Environment.CurrentManagedThreadId;
                    answered.Set();
                });
            }
            catch (Exception failure)
            {
                Log.Warning($"Slow: the screen's thread can no longer be watched", failure);
                return;
            }

            var seen = new List<string>();
            do
            {
                if (doing is var now && (seen.Count == 0 || seen[^1] != now) && seen.Count < MostSteps)
                {
                    seen.Add(now);
                }
            }
            while (!answered.Wait(CheckMs));

            var took = asked.ElapsedMilliseconds;
            if (took >= SlowMs)
            {
                var since = Collections();
                Log.Warning($"Slow: the screen's thread did not answer for {took} ms, in: {string.Join(" › ", seen)}; "
                    + $"memory {Environment.WorkingSet / (1024 * 1024)} MB, .NET's {GC.GetTotalMemory(false) / (1024 * 1024)} MB, "
                    + $"garbage collections since the line before {since.Young - collections.Young}/{since.Middle - collections.Middle}/{since.Old - collections.Old}");
                collections = since;
            }

            Thread.Sleep(CheckMs);
        }
    }

    private static (int Young, int Middle, int Old) Collections() =>
        (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
}
