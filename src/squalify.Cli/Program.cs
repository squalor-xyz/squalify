// SPDX-License-Identifier: Apache-2.0
using System.Text;
using squalor.squalify.Cli;

// The descriptor is UTF-8 whatever the console code page is, so stdout is a UTF-8 writer without a BOM.
using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
var code = await CommandFactory.InvokeAsync(args, stdout, Console.Error);
await stdout.FlushAsync();
return code;
