// Copyright (c) Alexander Nachtmann
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace Qyl.Telemetry.SemanticConventions.Analyzers.DocsGenerator;

/// <summary>
///   Class-name ↔ symbolic-name ↔ on-disk-filename transforms. Kept in one place because
///   the per-rule docs filename (<c>docs/rules/{id}_{symbolic}.md</c>), the descriptor
///   <c>HelpLinkUri</c> anchor, and the GitHub-case-sensitive source filename all have
///   to agree byte-for-byte for <c>--check</c> drift detection to mean anything.
/// </summary>
internal static partial class SymbolicNaming
{
    /// <summary>
    ///   Maps an analyzer class name to its on-disk source filename. Reflected class
    ///   names use Pascal-case <c>Qyl</c> (e.g., <c>Qyl0006MissingSchemaUrlAnalyzer</c>),
    ///   but git tracks the files with uppercase <c>QYL</c> prefix
    ///   (<c>QYL0006MissingSchemaUrlAnalyzer.cs</c>). macOS's case-insensitive default
    ///   masks this locally; GitHub's case-sensitive URL space does not. This lifts
    ///   <c>Qyl{4-digit}</c> to <c>QYL{4-digit}</c> so per-rule Source links resolve
    ///   on GitHub. Non-prefixed names (e.g., <c>GraphqlDocumentOptInAnalyzer</c>) pass
    ///   through unchanged because the class name already matches the file name.
    /// </summary>
    public static string FileBasenameForClass(string className)
    {
        var m = FileBasenamePrefixRegex().Match(className);
        return m.Success ? $"QYL{m.Groups[1].Value}{m.Groups[2].Value}" : className;
    }

    [GeneratedRegex(@"^Qyl(\d{4})(.*)$")]
    private static partial Regex FileBasenamePrefixRegex();
}
