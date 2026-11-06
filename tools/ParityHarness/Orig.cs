using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace ParityHarness;

/// <summary>Reflection helpers for driving the original (decompiled-equivalent) MainForm.</summary>
internal static class Orig
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static object Get(object target, string name)
    {
        var type = target as Type ?? target.GetType();
        var instance = target is Type ? null : target;
        var field = type.GetField(name, All);
        if (field != null) return field.GetValue(instance);
        var prop = type.GetProperty(name, All);
        if (prop != null) return prop.GetValue(instance);
        throw new MissingMemberException(type.Name, name);
    }

    public static T Get<T>(object target, string name) => (T)Get(target, name);

    public static void Set(object target, string name, object value)
    {
        var type = target as Type ?? target.GetType();
        var instance = target is Type ? null : target;
        var field = type.GetField(name, All);
        if (field != null) { field.SetValue(instance, value); return; }
        var prop = type.GetProperty(name, All);
        if (prop != null) { prop.SetValue(instance, value); return; }
        throw new MissingMemberException(type.Name, name);
    }

    public static object Call(object target, string name, params object[] args)
    {
        var type = target as Type ?? target.GetType();
        var instance = target is Type ? null : target;
        foreach (var m in type.GetMethods(All))
        {
            if (m.Name == name && m.GetParameters().Length == (args?.Length ?? 0))
            {
                try { return m.Invoke(instance, args); }
                catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }
            }
        }
        throw new MissingMethodException(type.Name, name);
    }

    /// <summary>Invokes an event handler such as <c>Dat_LoadDAT_Click(object, EventArgs)</c>.</summary>
    public static void Click(object form, string handler) => Call(form, handler, null, EventArgs.Empty);

    public static T Ctl<T>(object form, string name) where T : class => (T)Get(form, name);

    /// <summary>Pumps the message loop for <paramref name="ms"/> milliseconds.</summary>
    public static void Pump(int ms = 50)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        do { Application.DoEvents(); Thread.Sleep(5); } while (DateTime.UtcNow < until);
    }

    /// <summary>Pumps the message loop until <paramref name="condition"/> holds or the timeout elapses.</summary>
    public static bool PumpUntil(Func<bool> condition, int timeoutMs = 600_000)
    {
        // No sleep: the original loader marshals one Invoke per appearance (~60k), so any
        // delay between DoEvents calls multiplies into minutes.
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("Original application did not reach the expected state.");
            Application.DoEvents();
            Thread.Yield();
        }
        return true;
    }

    public static int Count(object list) => ((ICollection)list).Count;
}
