using CliFx;

return await new CliApplicationBuilder()
    .SetTitle("m365-mail-mirror")
    .SetDescription("Archive Microsoft 365 mailboxes to local storage with EML-first architecture")
    .SetVersion("1.0.0")
    .AddCommandsFromThisAssembly()
    .Build()
    .RunAsync();
