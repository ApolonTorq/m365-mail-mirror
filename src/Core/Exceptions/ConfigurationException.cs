namespace M365MailMirror.Core.Exceptions;

/// <summary>
/// Exception thrown when configuration loading or validation fails.
/// This includes YAML parsing errors, missing required values, and invalid settings.
/// </summary>
public class ConfigurationException : M365MailMirrorException
{
    /// <summary>
    /// Gets the path to the configuration file that caused the error, if known.
    /// </summary>
    public string? ConfigFilePath { get; }

    /// <summary>
    /// Initializes a new instance with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="configFilePath">The path to the configuration file, if applicable.</param>
    public ConfigurationException(string message, string? configFilePath = null)
        : base(message, CliExitCodes.ConfigurationError)
    {
        ConfigFilePath = configFilePath;
    }

    /// <summary>
    /// Initializes a new instance with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception that caused this exception.</param>
    /// <param name="configFilePath">The path to the configuration file, if applicable.</param>
    public ConfigurationException(string message, Exception innerException, string? configFilePath = null)
        : base(message, innerException, CliExitCodes.ConfigurationError)
    {
        ConfigFilePath = configFilePath;
    }
}
