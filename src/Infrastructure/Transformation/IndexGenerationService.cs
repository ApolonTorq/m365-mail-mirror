using System.Diagnostics;
using System.Globalization;
using System.Text;
using M365MailMirror.Core.Database;
using M365MailMirror.Core.Database.Entities;
using M365MailMirror.Core.Logging;
using M365MailMirror.Core.Transform;

namespace M365MailMirror.Infrastructure.Transform;

/// <summary>
/// Service for generating index files (index.html and index.md) for navigating the email archive.
/// Creates index files at each level of the hierarchy: root, years, and months.
/// </summary>
public class IndexGenerationService : IIndexGenerationService
{
    private readonly IStateDatabase _database;
    private readonly string _archiveRoot;
    private readonly IAppLogger _logger;

    /// <summary>
    /// Creates a new IndexGenerationService.
    /// </summary>
    public IndexGenerationService(
        IStateDatabase database,
        string archiveRoot,
        IAppLogger? logger = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _archiveRoot = archiveRoot ?? throw new ArgumentNullException(nameof(archiveRoot));
        _logger = logger ?? LoggerFactory.CreateLogger<IndexGenerationService>();
    }

    /// <inheritdoc />
    public async Task<IndexGenerationResult> GenerateIndexesAsync(
        IndexGenerationOptions options,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var htmlCount = 0;
        var markdownCount = 0;
        var errors = 0;

        try
        {
            _logger.Info("Starting index generation...");

            // Get all distinct year-month combinations
            var yearMonths = await _database.GetDistinctYearMonthsAsync(cancellationToken);
            _logger.Debug("Found {0} distinct year-month combinations", yearMonths.Count);

            if (yearMonths.Count == 0)
            {
                _logger.Info("No messages found, skipping index generation");
                stopwatch.Stop();
                return IndexGenerationResult.Successful(0, 0, 0, stopwatch.Elapsed);
            }

            // Build the hierarchy structure (flat year/month)
            var hierarchy = await BuildHierarchyAsync(yearMonths, cancellationToken);

            // Generate indexes at each level
            if (options.GenerateHtmlIndexes)
            {
                htmlCount = await GenerateHtmlIndexesAsync(hierarchy, cancellationToken);
                _logger.Info("Generated {0} HTML index files", htmlCount);
            }

            if (options.GenerateMarkdownIndexes)
            {
                markdownCount = await GenerateMarkdownIndexesAsync(hierarchy, cancellationToken);
                _logger.Info("Generated {0} Markdown index files", markdownCount);
            }

            stopwatch.Stop();
            _logger.Info("Index generation completed in {0}", stopwatch.Elapsed);
            return IndexGenerationResult.Successful(htmlCount, markdownCount, errors, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.Warning("Index generation cancelled after {0}", stopwatch.Elapsed);
            return IndexGenerationResult.Failed("Index generation was cancelled", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.Error(ex, "Index generation failed: {0}", ex.Message);
            return IndexGenerationResult.Failed(ex.Message, stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Builds the year/month hierarchy structure from distinct year-month combinations.
    /// </summary>
    private async Task<IndexNode> BuildHierarchyAsync(
        IReadOnlyList<(int Year, int Month)> yearMonths,
        CancellationToken cancellationToken)
    {
        var root = new IndexNode
        {
            Name = "Archive",
            Path = "",
            NodeType = IndexNodeType.Root
        };

        // Group by year
        var yearGroups = yearMonths.GroupBy(ym => ym.Year).OrderByDescending(g => g.Key);

        foreach (var yearGroup in yearGroups)
        {
            var yearNode = new IndexNode
            {
                Name = yearGroup.Key.ToString(CultureInfo.InvariantCulture),
                Path = yearGroup.Key.ToString(CultureInfo.InvariantCulture),
                NodeType = IndexNodeType.Year
            };

            foreach (var (year, month) in yearGroup.OrderByDescending(ym => ym.Month))
            {
                var messages = await _database.GetMessagesForIndexAsync(year, month, cancellationToken);
                var monthNode = new IndexNode
                {
                    Name = BreadcrumbHelper.GetMonthName(month),
                    Path = string.Concat(year.ToString(CultureInfo.InvariantCulture), "/", month.ToString("D2", CultureInfo.InvariantCulture)),
                    NodeType = IndexNodeType.Month,
                    TotalMessageCount = messages.Count
                };

                // Add message summaries
                foreach (var message in messages)
                {
                    var filename = Path.GetFileNameWithoutExtension(Path.GetFileName(message.LocalPath));
                    monthNode.Messages.Add(new MessageSummary
                    {
                        Subject = message.Subject ?? "(no subject)",
                        Sender = message.Sender ?? "",
                        ReceivedTime = message.ReceivedTime,
                        HasAttachments = message.HasAttachments,
                        HtmlFilename = string.Concat(filename, ".html"),
                        MarkdownFilename = string.Concat(filename, ".md")
                    });
                }

                yearNode.Children.Add(monthNode);
                yearNode.TotalMessageCount += monthNode.TotalMessageCount;
            }

            root.Children.Add(yearNode);
            root.TotalMessageCount += yearNode.TotalMessageCount;
        }

        return root;
    }

    /// <summary>
    /// Generates HTML index files for the entire hierarchy.
    /// </summary>
    private async Task<int> GenerateHtmlIndexesAsync(IndexNode root, CancellationToken cancellationToken)
    {
        var count = 0;

        // Generate root index in the unified transformed directory
        await GenerateHtmlIndexFileAsync(root, "transformed", cancellationToken);
        count++;

        // Recursively generate for all children
        count += await GenerateHtmlIndexesRecursiveAsync(root, "transformed", cancellationToken);

        return count;
    }

    private async Task<int> GenerateHtmlIndexesRecursiveAsync(
        IndexNode node,
        string basePath,
        CancellationToken cancellationToken)
    {
        var count = 0;

        foreach (var child in node.Children)
        {
            var childPath = string.IsNullOrEmpty(node.Path)
                ? string.Concat(basePath, "/", child.Name)
                : string.Concat(basePath, "/", child.Path);

            await GenerateHtmlIndexFileAsync(child, childPath, cancellationToken);
            count++;

            if (child.NodeType != IndexNodeType.Month)
            {
                count += await GenerateHtmlIndexesRecursiveAsync(child, basePath, cancellationToken);
            }
        }

        return count;
    }

    /// <summary>
    /// Generates a single HTML index file.
    /// </summary>
    private async Task GenerateHtmlIndexFileAsync(
        IndexNode node,
        string outputDir,
        CancellationToken cancellationToken)
    {
        var fullDir = Path.Combine(_archiveRoot, outputDir);
        Directory.CreateDirectory(fullDir);

        var indexPath = Path.Combine(fullDir, "index.html");
        var relativePath = string.Concat(outputDir, "/index.html");

        var breadcrumb = node.NodeType == IndexNodeType.Root
            ? "<span class=\"current\">Archive</span>"
            : BreadcrumbHelper.GenerateHtmlIndexBreadcrumb(relativePath);

        var title = GetNodeTitle(node);
        var content = GenerateHtmlContent(node);
        var upLink = GenerateHtmlUpLink(node);
        var stats = string.Create(CultureInfo.InvariantCulture, $"{node.TotalMessageCount} message{(node.TotalMessageCount != 1 ? "s" : "")}");

        var html = GenerateHtmlIndexTemplate(title, breadcrumb, upLink, content, stats);
        await File.WriteAllTextAsync(indexPath, html, cancellationToken);
    }

    /// <summary>
    /// Generates Markdown index files for the entire hierarchy.
    /// </summary>
    private async Task<int> GenerateMarkdownIndexesAsync(IndexNode root, CancellationToken cancellationToken)
    {
        var count = 0;

        // Generate root index in the unified transformed directory (same location as HTML)
        await GenerateMarkdownIndexFileAsync(root, "transformed", cancellationToken);
        count++;

        // Recursively generate for all children
        count += await GenerateMarkdownIndexesRecursiveAsync(root, "transformed", cancellationToken);

        return count;
    }

    private async Task<int> GenerateMarkdownIndexesRecursiveAsync(
        IndexNode node,
        string basePath,
        CancellationToken cancellationToken)
    {
        var count = 0;

        foreach (var child in node.Children)
        {
            var childPath = string.IsNullOrEmpty(node.Path)
                ? string.Concat(basePath, "/", child.Name)
                : string.Concat(basePath, "/", child.Path);

            await GenerateMarkdownIndexFileAsync(child, childPath, cancellationToken);
            count++;

            if (child.NodeType != IndexNodeType.Month)
            {
                count += await GenerateMarkdownIndexesRecursiveAsync(child, basePath, cancellationToken);
            }
        }

        return count;
    }

    /// <summary>
    /// Generates a single Markdown index file.
    /// </summary>
    private async Task GenerateMarkdownIndexFileAsync(
        IndexNode node,
        string outputDir,
        CancellationToken cancellationToken)
    {
        var fullDir = Path.Combine(_archiveRoot, outputDir);
        Directory.CreateDirectory(fullDir);

        var indexPath = Path.Combine(fullDir, "index.md");
        var relativePath = string.Concat(outputDir, "/index.md");

        var breadcrumb = node.NodeType == IndexNodeType.Root
            ? "**Archive**"
            : BreadcrumbHelper.GenerateMarkdownIndexBreadcrumb(relativePath);

        var title = GetNodeTitle(node);
        var content = GenerateMarkdownContent(node);
        var upLink = GenerateMarkdownUpLink(node);
        var stats = string.Create(CultureInfo.InvariantCulture, $"*{node.TotalMessageCount} message{(node.TotalMessageCount != 1 ? "s" : "")}*");

        var markdown = GenerateMarkdownIndexTemplate(title, breadcrumb, upLink, content, stats);
        await File.WriteAllTextAsync(indexPath, markdown, cancellationToken);
    }

    private static string GetNodeTitle(IndexNode node)
    {
        return node.NodeType switch
        {
            IndexNodeType.Root => "Mail Archive",
            IndexNodeType.MailFolder => node.Name,
            IndexNodeType.Year => node.Name,
            IndexNodeType.Month => node.Name,
            _ => node.Name
        };
    }

    private static string GenerateHtmlContent(IndexNode node)
    {
        var sb = new StringBuilder();

        if (node.NodeType == IndexNodeType.Month && node.Messages.Count > 0)
        {
            // Generate email table
            sb.AppendLine("<table class=\"email-table\">");
            sb.AppendLine("<thead><tr><th>Subject</th><th>From</th><th>Date</th><th></th></tr></thead>");
            sb.AppendLine("<tbody>");

            foreach (var message in node.Messages)
            {
                var attachmentIcon = message.HasAttachments ? "&#128206;" : "";
                var formattedDate = message.ReceivedTime.ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);
                sb.AppendLine("<tr>");
                sb.Append("<td><a href=\"");
                sb.Append(System.Net.WebUtility.HtmlEncode(message.HtmlFilename));
                sb.Append("\">");
                sb.Append(System.Net.WebUtility.HtmlEncode(message.Subject));
                sb.AppendLine("</a></td>");
                sb.Append("<td class=\"email-from\">");
                sb.Append(System.Net.WebUtility.HtmlEncode(message.Sender));
                sb.AppendLine("</td>");
                sb.Append("<td class=\"email-date\">");
                sb.Append(formattedDate);
                sb.AppendLine("</td>");
                sb.Append("<td class=\"attachment-icon\">");
                sb.Append(attachmentIcon);
                sb.AppendLine("</td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody></table>");
        }
        else if (node.Children.Count > 0)
        {
            // Generate folder/year/month list
            sb.AppendLine("<ul class=\"folder-list\">");

            foreach (var child in node.Children)
            {
                var icon = child.NodeType switch
                {
                    IndexNodeType.MailFolder => "&#128193;",
                    IndexNodeType.Year => "&#128197;",
                    IndexNodeType.Month => "&#128197;",
                    _ => "&#128193;"
                };

                // Use the last segment of Path (actual folder name) not Name (display name)
                // This handles months where Name="January" but Path ends in "01"
                var folderSegment = child.Path.Split('/', '\\').Last();
                var linkPath = string.Concat(folderSegment, "/index.html");

                var countText = child.TotalMessageCount > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"<span class=\"count\">({child.TotalMessageCount})</span>")
                    : "";

                sb.Append("<li class=\"folder-item\"><span class=\"folder-icon\">");
                sb.Append(icon);
                sb.Append("</span><a href=\"");
                sb.Append(linkPath);
                sb.Append("\">");
                sb.Append(System.Net.WebUtility.HtmlEncode(child.Name));
                sb.Append("</a> ");
                sb.Append(countText);
                sb.AppendLine("</li>");
            }

            sb.AppendLine("</ul>");
        }
        else
        {
            sb.AppendLine("<p class=\"empty\">No messages in this folder.</p>");
        }

        return sb.ToString();
    }

    private static string GenerateHtmlUpLink(IndexNode node)
    {
        if (node.NodeType == IndexNodeType.Root)
            return "";

        return "<div class=\"up-link\"><a href=\"../index.html\">&#8593; Up</a></div>";
    }

    private static string GenerateMarkdownContent(IndexNode node)
    {
        var sb = new StringBuilder();

        if (node.NodeType == IndexNodeType.Month && node.Messages.Count > 0)
        {
            // Generate email table
            sb.AppendLine("| Subject | From | Date | |");
            sb.AppendLine("|---------|------|------|-|");

            foreach (var message in node.Messages)
            {
                var attachmentIcon = message.HasAttachments ? "📎" : "";
                var formattedDate = message.ReceivedTime.ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);
                sb.Append("| [");
                sb.Append(EscapeMarkdown(message.Subject));
                sb.Append("](");
                sb.Append(message.MarkdownFilename);
                sb.Append(") | ");
                sb.Append(EscapeMarkdown(message.Sender));
                sb.Append(" | ");
                sb.Append(formattedDate);
                sb.Append(" | ");
                sb.Append(attachmentIcon);
                sb.AppendLine(" |");
            }
        }
        else if (node.Children.Count > 0)
        {
            // Generate folder/year/month list
            foreach (var child in node.Children)
            {
                var icon = child.NodeType switch
                {
                    IndexNodeType.MailFolder => "📁",
                    IndexNodeType.Year => "📅",
                    IndexNodeType.Month => "📅",
                    _ => "📁"
                };

                // Use the last segment of Path (actual folder name) not Name (display name)
                // This handles months where Name="January" but Path ends in "01"
                var folderSegment = child.Path.Split('/', '\\').Last();
                var linkPath = string.Concat(folderSegment, "/index.md");
                var countText = child.TotalMessageCount > 0
                    ? string.Create(CultureInfo.InvariantCulture, $" ({child.TotalMessageCount})")
                    : "";
                sb.Append("- ");
                sb.Append(icon);
                sb.Append(" [");
                sb.Append(child.Name);
                sb.Append("](");
                sb.Append(linkPath);
                sb.Append(')');
                sb.AppendLine(countText);
            }
        }
        else
        {
            sb.AppendLine("*No messages in this folder.*");
        }

        return sb.ToString();
    }

    private static string GenerateMarkdownUpLink(IndexNode node)
    {
        if (node.NodeType == IndexNodeType.Root)
            return "";

        return "[↑ Up](../index.md)\n";
    }

    private static string EscapeMarkdown(string text)
    {
        // Escape pipe characters for table cells
        return text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal);
    }

    private static string GenerateHtmlIndexTemplate(
        string title,
        string breadcrumb,
        string upLink,
        string content,
        string stats)
    {
        return string.Create(CultureInfo.InvariantCulture, $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{System.Net.WebUtility.HtmlEncode(title)}</title>
    <style>
        * {{ box-sizing: border-box; }}
        body {{
            font-family: 'Segoe UI', -apple-system, BlinkMacSystemFont, sans-serif;
            margin: 0; padding: 0; background: #f5f5f5;
        }}
        .container {{
            max-width: 1000px; margin: 20px auto;
            background: #fff; border-radius: 4px;
            box-shadow: 0 1px 3px rgba(0,0,0,0.12);
        }}
        .header {{
            background: #0078d4; color: white;
            padding: 20px 25px; border-radius: 4px 4px 0 0;
        }}
        .header h1 {{ margin: 0; font-size: 1.5em; font-weight: 600; }}
        .breadcrumb {{
            padding: 12px 25px; background: #f8f9fa;
            border-bottom: 1px solid #edebe9; font-size: 0.9em;
        }}
        .breadcrumb a {{ color: #0078d4; text-decoration: none; }}
        .breadcrumb a:hover {{ text-decoration: underline; }}
        .breadcrumb .current {{ color: #605e5c; }}
        .content {{ padding: 0; }}
        .up-link {{
            padding: 12px 25px; border-bottom: 1px solid #edebe9;
            background: #faf9f8;
        }}
        .up-link a {{ color: #0078d4; text-decoration: none; }}
        .folder-list {{ list-style: none; padding: 0; margin: 0; }}
        .folder-item {{
            padding: 12px 25px; border-bottom: 1px solid #edebe9;
            display: flex; align-items: center;
        }}
        .folder-item:hover {{ background: #f5f5f5; }}
        .folder-item a {{ color: #323130; text-decoration: none; }}
        .folder-item a:hover {{ color: #0078d4; }}
        .folder-icon {{ margin-right: 12px; }}
        .count {{ color: #a19f9d; margin-left: 8px; font-size: 0.9em; }}
        .email-table {{ width: 100%; border-collapse: collapse; }}
        .email-table th {{
            text-align: left; padding: 10px 15px;
            background: #faf9f8; border-bottom: 1px solid #edebe9;
            font-weight: 600; color: #605e5c;
        }}
        .email-table td {{
            padding: 10px 15px; border-bottom: 1px solid #edebe9;
        }}
        .email-table tr:hover {{ background: #f5f5f5; }}
        .email-table a {{ color: #323130; text-decoration: none; }}
        .email-table a:hover {{ color: #0078d4; }}
        .email-from {{ color: #605e5c; }}
        .email-date {{ color: #a19f9d; white-space: nowrap; }}
        .attachment-icon {{ color: #797775; text-align: center; }}
        .stats {{ padding: 15px 25px; color: #605e5c; font-size: 0.85em; border-top: 1px solid #edebe9; }}
        .empty {{ padding: 25px; color: #605e5c; text-align: center; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header""><h1>{System.Net.WebUtility.HtmlEncode(title)}</h1></div>
        <nav class=""breadcrumb"">{breadcrumb}</nav>
        {upLink}
        <div class=""content"">
            {content}
        </div>
        <div class=""stats"">{stats}</div>
    </div>
</body>
</html>");
    }

    private static string GenerateMarkdownIndexTemplate(
        string title,
        string breadcrumb,
        string upLink,
        string content,
        string stats)
    {
        return string.Create(CultureInfo.InvariantCulture, $@"# {title}

{breadcrumb}

{upLink}
---

{content}

---

{stats}
");
    }
}
