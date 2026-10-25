namespace Berpiztu.KeyPlus;

/// <summary>
/// What Spelling reads from its files (the accent model, the dictionary, the
/// common misspellings), read away from the window's thread the first time
/// it is asked for, and read again once its <paramref name="version"/>
/// changes (files downloaded again, a choice turned on); meanwhile the one
/// read last stays in use, so nothing goes unchecked while it is read, and
/// it is let go once the new one is in. A version of none: nothing to read.
/// </summary>
internal sealed class Loaded<T>(Func<object?> version, Func<T?> load, Action<T>? letGo = null)
    where T : class
{
    private Task<T?>? reading;
    private object? readVersion;
    private T? inUse;

    /// <summary>The reading, begun again where its version changed or it failed.</summary>
    public Task<T?> Async()
    {
        var now = version();
        if (reading is null || (reading.IsCompleted && now is not null && (!reading.IsCompletedSuccessfully || reading.Result is null || !Equals(now, readVersion))))
        {
            var before = reading;
            readVersion = now;
            reading = Task.Run(() =>
            {
                var read = load();
                if (letGo is not null && before is { IsCompletedSuccessfully: true, Result: { } old } && !ReferenceEquals(old, read))
                {
                    letGo(old);
                }

                return read;
            });
        }

        return reading;
    }

    /// <summary>The one read last; none until the first is read.</summary>
    public T? InUse()
    {
        if (Async() is { IsCompletedSuccessfully: true, Result: { } read })
        {
            inUse = read;
        }

        return inUse;
    }
}
