#r "src/Infrastructure/bin/Debug/net10.0/M365MailMirror.Infrastructure.dll"
using M365MailMirror.Infrastructure.Transform;

var input = @"<ol>
<li>Please provide bank statements:</li>
</ol>
<ul style=""margin-left:18pt"">
<li>ANZ #6773</li>
<li>ANZ Visa #4176</li>
</ul>";

var result = MarkdownCleaningHelper.ConvertHtmlToMarkdown(input);
Console.WriteLine("=== OUTPUT ===");
Console.WriteLine(result);
Console.WriteLine("=== END ===");
