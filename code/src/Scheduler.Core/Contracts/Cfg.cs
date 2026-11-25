// Copyright (c) 2026 Trevor W. Walker. Licensed under the PolyForm Noncommercial License 1.0.0; see LICENSE.
// Required Notice: Copyright (c) 2026 Trevor W. Walker (https://github.com/trevorwwalker)
namespace Scheduler.Core.Contracts;

/// <summary>
/// The script's <c>Cfg(value, default)</c> (CFG-5): null or an empty string means the default;
/// <c>0</c> and <c>false</c> are values.
/// </summary>
public static class Cfg
{
    /// <summary>A text setting: null or "" gives <paramref name="fallback"/>.</summary>
    public static string Value(string? value, string fallback) =>
        string.IsNullOrEmpty(value) ? fallback : value;

    /// <summary>A number or switch: null gives <paramref name="fallback"/>; 0 and false are kept.</summary>
    public static T Value<T>(T? value, T fallback) where T : struct =>
        value ?? fallback;

    /// <summary>An untyped value, as read from JSON: null or "" gives <paramref name="fallback"/>.</summary>
    public static object? Value(object? value, object? fallback) =>
        value is null || value is string { Length: 0 } ? fallback : value;
}
