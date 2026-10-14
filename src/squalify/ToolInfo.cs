// SPDX-License-Identifier: Apache-2.0
using System.Reflection;

namespace squalor.squalify;

/// <summary>Identity of the tool that writes a descriptor.</summary>
public static class ToolInfo
{
    /// <summary>The tool name.</summary>
    public const string Name = "squalify";

    /// <summary>The tool version, read from the assembly's informational version.</summary>
    public static string Version { get; } =
        typeof(ToolInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";
}
