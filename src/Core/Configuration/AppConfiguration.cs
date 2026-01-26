namespace M365MailMirror.Core.Configuration;

/// <summary>
/// Root configuration model for the m365-mail-mirror application.
/// </summary>
public class AppConfiguration
{
    /// <summary>
    /// Azure AD client ID for authentication.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Azure AD tenant ID. Defaults to "common" for multi-tenant apps.
    /// </summary>
    public string TenantId { get; set; } = "common";

    /// <summary>
    /// Optional mailbox to access (defaults to authenticated user's mailbox).
    /// </summary>
    public string? Mailbox { get; set; }

    /// <summary>
    /// Output directory for the mail archive.
    /// </summary>
    public string OutputPath { get; set; } = ".";

    /// <summary>
    /// Sync configuration settings.
    /// </summary>
    public SyncConfiguration Sync { get; set; } = new();

    /// <summary>
    /// Transformation configuration settings.
    /// </summary>
    public TransformConfiguration Transform { get; set; } = new();

    /// <summary>
    /// Attachment extraction configuration settings.
    /// </summary>
    public AttachmentConfiguration Attachments { get; set; } = new();

    /// <summary>
    /// ZIP extraction configuration settings.
    /// </summary>
    public ZipExtractionConfiguration ZipExtraction { get; set; } = new();
}

/// <summary>
/// Configuration for sync operations.
/// </summary>
public class SyncConfiguration
{
    /// <summary>
    /// Number of messages after which to checkpoint progress during streaming sync.
    /// Lower values provide finer recovery granularity but more database writes.
    /// </summary>
    public int CheckpointInterval { get; set; } = 50;

    /// <summary>
    /// Number of parallel downloads.
    /// </summary>
    public int Parallel { get; set; } = 5;

    /// <summary>
    /// Folders to exclude from sync.
    /// </summary>
    public List<string> ExcludeFolders { get; set; } = [];

    /// <summary>
    /// Overlap period in minutes for date-based catchup sync.
    /// </summary>
    public int OverlapMinutes { get; set; } = 60;
}

/// <summary>
/// Configuration for transformation operations.
/// </summary>
public class TransformConfiguration
{
    /// <summary>
    /// Whether to generate HTML transformations. Defaults to true.
    /// </summary>
    public bool GenerateHtml { get; set; } = true;

    /// <summary>
    /// Whether to generate Markdown transformations. Defaults to false.
    /// </summary>
    public bool GenerateMarkdown { get; set; }

    /// <summary>
    /// Whether to extract attachments. Defaults to true.
    /// </summary>
    public bool ExtractAttachments { get; set; } = true;

    /// <summary>
    /// HTML-specific transformation settings.
    /// </summary>
    public HtmlTransformConfiguration Html { get; set; } = new();
}

/// <summary>
/// Configuration for HTML transformations.
/// </summary>
public class HtmlTransformConfiguration
{
    /// <summary>
    /// Whether to inline CSS styles in each HTML file. Defaults to false.
    /// </summary>
    public bool InlineStyles { get; set; }

    /// <summary>
    /// Whether to strip external images from HTML. Defaults to false.
    /// </summary>
    public bool StripExternalImages { get; set; }

    /// <summary>
    /// Whether to hide CC recipients in HTML output. Defaults to false.
    /// </summary>
    public bool HideCc { get; set; }

    /// <summary>
    /// Whether to hide BCC recipients in HTML output. Defaults to true.
    /// </summary>
    public bool HideBcc { get; set; } = true;

    /// <summary>
    /// Whether to include a "View in Outlook" link in HTML/Markdown output. Defaults to true.
    /// When enabled, adds a clickable link that opens the email in Outlook Web.
    /// </summary>
    public bool IncludeOutlookLink { get; set; } = true;
}

/// <summary>
/// Configuration for attachment extraction.
/// </summary>
public class AttachmentConfiguration
{
    /// <summary>
    /// Whether to skip executable file extraction for security. Defaults to true.
    /// </summary>
    public bool SkipExecutables { get; set; } = true;
}

/// <summary>
/// Configuration for ZIP file extraction.
/// </summary>
public class ZipExtractionConfiguration
{
    /// <summary>
    /// Whether to auto-extract ZIP file contents. Defaults to true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum number of files to extract from a ZIP (skip if fewer).
    /// </summary>
    public int MinFiles { get; set; } = 1;

    /// <summary>
    /// Maximum number of files to extract from a ZIP (skip if more).
    /// </summary>
    public int MaxFiles { get; set; } = 100;

    /// <summary>
    /// Whether to skip password-protected ZIPs. Defaults to true.
    /// </summary>
    public bool SkipEncrypted { get; set; } = true;

    /// <summary>
    /// Whether to skip ZIPs containing executable files. Defaults to true.
    /// </summary>
    public bool SkipWithExecutables { get; set; } = true;
}
