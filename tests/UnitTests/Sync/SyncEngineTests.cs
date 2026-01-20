using M365MailMirror.Core.Database;
using M365MailMirror.Core.Database.Entities;
using M365MailMirror.Core.Graph;
using M365MailMirror.Core.Storage;
using M365MailMirror.Core.Sync;
using M365MailMirror.Infrastructure.Sync;
using Moq;

namespace M365MailMirror.UnitTests.Sync;

/// <summary>
/// Unit tests for the SyncEngine class.
/// </summary>
public class SyncEngineTests
{
    private readonly Mock<IGraphMailClient> _mockGraphClient;
    private readonly Mock<IStateDatabase> _mockDatabase;
    private readonly Mock<IEmlStorageService> _mockEmlStorage;
    private readonly SyncEngine _syncEngine;

    public SyncEngineTests()
    {
        _mockGraphClient = new Mock<IGraphMailClient>();
        _mockDatabase = new Mock<IStateDatabase>();
        _mockEmlStorage = new Mock<IEmlStorageService>();

        _syncEngine = new SyncEngine(
            _mockGraphClient.Object,
            _mockDatabase.Object,
            _mockEmlStorage.Object);
    }

    #region Basic Sync Tests

    [Fact]
    public async Task SyncAsync_EmptyMailbox_ReturnsSuccessWithZeroMessages()
    {
        // Arrange
        _mockGraphClient.Setup(x => x.GetUserEmailAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("user@example.com");

        _mockDatabase.Setup(x => x.GetSyncStateAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);

        _mockGraphClient.Setup(x => x.GetFoldersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppMailFolder>());

        var options = new SyncOptions();

        // Act
        var result = await _syncEngine.SyncAsync(options);

        // Assert
        result.Success.Should().BeTrue();
        result.MessagesSynced.Should().Be(0);
        result.FoldersProcessed.Should().Be(0);
    }

    [Fact]
    public async Task SyncAsync_SingleFolderWithMessages_SyncsAllMessages()
    {
        // Arrange
        SetupBasicMocks();

        var folders = new List<AppMailFolder>
        {
            CreateTestFolder("folder-1", "Inbox", 3)
        };

        _mockGraphClient.Setup(x => x.GetFoldersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folders);

        var messages = new List<MessageInfo>
        {
            CreateTestMessageInfo("msg-1", "Subject 1"),
            CreateTestMessageInfo("msg-2", "Subject 2"),
            CreateTestMessageInfo("msg-3", "Subject 3")
        };

        _mockGraphClient.Setup(x => x.GetMessagesDeltaAsync("folder-1", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltaQueryResult<MessageInfo>
            {
                Items = messages,
                HasMorePages = false,
                DeltaToken = "delta-token-1"
            });

        _mockDatabase.Setup(x => x.GetMessageByImmutableIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message?)null);

        SetupMimeDownloadMock();

        var options = new SyncOptions { BatchSize = 100 };

        // Act
        var result = await _syncEngine.SyncAsync(options);

        // Assert
        result.Success.Should().BeTrue();
        result.MessagesSynced.Should().Be(3);
        result.FoldersProcessed.Should().Be(1);

        _mockDatabase.Verify(
            x => x.InsertMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task SyncAsync_DryRun_DoesNotWriteToDatabase()
    {
        // Arrange
        SetupBasicMocks();

        var folders = new List<AppMailFolder>
        {
            CreateTestFolder("folder-1", "Inbox", 2)
        };

        _mockGraphClient.Setup(x => x.GetFoldersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folders);

        var messages = new List<MessageInfo>
        {
            CreateTestMessageInfo("msg-1", "Subject 1"),
            CreateTestMessageInfo("msg-2", "Subject 2")
        };

        _mockGraphClient.Setup(x => x.GetMessagesDeltaAsync("folder-1", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltaQueryResult<MessageInfo>
            {
                Items = messages,
                HasMorePages = false
            });

        _mockDatabase.Setup(x => x.GetMessageByImmutableIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message?)null);

        var options = new SyncOptions { DryRun = true };

        // Act
        var result = await _syncEngine.SyncAsync(options);

        // Assert
        result.Success.Should().BeTrue();
        result.IsDryRun.Should().BeTrue();
        result.MessagesSynced.Should().Be(2);

        _mockDatabase.Verify(
            x => x.InsertMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _mockGraphClient.Verify(
            x => x.DownloadMessageMimeAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncAsync_ExistingMessage_SkipsMessage()
    {
        // Arrange
        SetupBasicMocks();

        var folders = new List<AppMailFolder>
        {
            CreateTestFolder("folder-1", "Inbox", 2)
        };

        _mockGraphClient.Setup(x => x.GetFoldersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folders);

        var messages = new List<MessageInfo>
        {
            CreateTestMessageInfo("msg-1", "Subject 1"),
            CreateTestMessageInfo("msg-2", "Subject 2")
        };

        _mockGraphClient.Setup(x => x.GetMessagesDeltaAsync("folder-1", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltaQueryResult<MessageInfo>
            {
                Items = messages,
                HasMorePages = false
            });

        // First message already exists
        _mockDatabase.Setup(x => x.GetMessageByImmutableIdAsync("msg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestMessage("msg-1"));

        // Second message is new
        _mockDatabase.Setup(x => x.GetMessageByImmutableIdAsync("msg-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message?)null);

        SetupMimeDownloadMock();

        var options = new SyncOptions();

        // Act
        var result = await _syncEngine.SyncAsync(options);

        // Assert
        result.Success.Should().BeTrue();
        result.MessagesSynced.Should().Be(1);
        result.MessagesSkipped.Should().Be(1);

        _mockDatabase.Verify(
            x => x.InsertMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Batch Processing Tests

    [Fact]
    public async Task SyncAsync_LargeFolderWithBatching_ProcessesInBatches()
    {
        // Arrange
        SetupBasicMocks();

        var folders = new List<AppMailFolder>
        {
            CreateTestFolder("folder-1", "Inbox", 5)
        };

        _mockGraphClient.Setup(x => x.GetFoldersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folders);

        var messages = Enumerable.Range(1, 5)
            .Select(i => CreateTestMessageInfo($"msg-{i}", $"Subject {i}"))
            .ToList();

        _mockGraphClient.Setup(x => x.GetMessagesDeltaAsync("folder-1", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltaQueryResult<MessageInfo>
            {
                Items = messages,
                HasMorePages = false
            });

        _mockDatabase.Setup(x => x.GetMessageByImmutableIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message?)null);

        SetupMimeDownloadMock();

        // Use batch size of 2, so 5 messages = 3 batches
        var options = new SyncOptions { BatchSize = 2 };

        // Act
        var result = await _syncEngine.SyncAsync(options);

        // Assert
        result.Success.Should().BeTrue();
        result.MessagesSynced.Should().Be(5);

        // Verify checkpointing happened (sync state updated for each batch)
        _mockDatabase.Verify(
            x => x.UpsertSyncStateAsync(It.IsAny<SyncState>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(3));
    }

    [Fact]
    public async Task SyncAsync_MultiplePages_ProcessesAllPages()
    {
        // Arrange
        SetupBasicMocks();

        var folders = new List<AppMailFolder>
        {
            CreateTestFolder("folder-1", "Inbox", 4)
        };

        _mockGraphClient.Setup(x => x.GetFoldersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folders);

        // First page
        var page1Messages = new List<MessageInfo>
        {
            CreateTestMessageInfo("msg-1", "Subject 1"),
            CreateTestMessageInfo("msg-2", "Subject 2")
        };

        // Second page
        var page2Messages = new List<MessageInfo>
        {
            CreateTestMessageInfo("msg-3", "Subject 3"),
            CreateTestMessageInfo("msg-4", "Subject 4")
        };

        var callCount = 0;
        _mockGraphClient.Setup(x => x.GetMessagesDeltaAsync("folder-1", It.IsAny<string?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return new DeltaQueryResult<MessageInfo>
                    {
                        Items = page1Messages,
                        HasMorePages = true,
                        NextPageLink = "next-page-url"
                    };
                }
                else
                {
                    return new DeltaQueryResult<MessageInfo>
                    {
                        Items = page2Messages,
                        HasMorePages = false,
                        DeltaToken = "final-delta-token"
                    };
                }
            });

        _mockDatabase.Setup(x => x.GetMessageByImmutableIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message?)null);

        SetupMimeDownloadMock();

        var options = new SyncOptions();

        // Act
        var result = await _syncEngine.SyncAsync(options);

        // Assert
        result.Success.Should().BeTrue();
        result.MessagesSynced.Should().Be(4);

        _mockGraphClient.Verify(
            x => x.GetMessagesDeltaAsync("folder-1", It.IsAny<string?>(), null, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    #endregion

    #region Folder Exclusion Tests

    [Fact]
    public async Task SyncAsync_WithExcludedFolders_SkipsExcludedFolders()
    {
        // Arrange
        SetupBasicMocks();

        var folders = new List<AppMailFolder>
        {
            CreateTestFolder("folder-1", "Inbox", 2),
            CreateTestFolder("folder-2", "Deleted Items", 5),
            CreateTestFolder("folder-3", "Junk Email", 3)
        };

        _mockGraphClient.Setup(x => x.GetFoldersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folders);

        // Only Inbox should be processed
        _mockGraphClient.Setup(x => x.GetMessagesDeltaAsync("folder-1", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltaQueryResult<MessageInfo>
            {
                Items = new List<MessageInfo> { CreateTestMessageInfo("msg-1", "Test") },
                HasMorePages = false
            });

        _mockDatabase.Setup(x => x.GetMessageByImmutableIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message?)null);

        SetupMimeDownloadMock();

        var options = new SyncOptions
        {
            ExcludeFolders = ["Deleted Items", "Junk Email"]
        };

        // Act
        var result = await _syncEngine.SyncAsync(options);

        // Assert
        result.Success.Should().BeTrue();
        result.FoldersProcessed.Should().Be(1);
        result.MessagesSynced.Should().Be(1);

        // Verify excluded folders were not accessed
        _mockGraphClient.Verify(
            x => x.GetMessagesDeltaAsync("folder-2", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _mockGraphClient.Verify(
            x => x.GetMessagesDeltaAsync("folder-3", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task SyncAsync_MessageDownloadFails_ContinuesWithOtherMessages()
    {
        // Arrange
        SetupBasicMocks();

        var folders = new List<AppMailFolder>
        {
            CreateTestFolder("folder-1", "Inbox", 3)
        };

        _mockGraphClient.Setup(x => x.GetFoldersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folders);

        var messages = new List<MessageInfo>
        {
            CreateTestMessageInfo("msg-1", "Subject 1"),
            CreateTestMessageInfo("msg-2", "Subject 2 - Will fail"),
            CreateTestMessageInfo("msg-3", "Subject 3")
        };

        _mockGraphClient.Setup(x => x.GetMessagesDeltaAsync("folder-1", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltaQueryResult<MessageInfo>
            {
                Items = messages,
                HasMorePages = false
            });

        _mockDatabase.Setup(x => x.GetMessageByImmutableIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message?)null);

        // msg-1 and msg-3 succeed, msg-2 fails
        _mockGraphClient.Setup(x => x.DownloadMessageMimeAsync("msg-1", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMimeStream("test content"));

        _mockGraphClient.Setup(x => x.DownloadMessageMimeAsync("msg-2", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Download failed"));

        _mockGraphClient.Setup(x => x.DownloadMessageMimeAsync("msg-3", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMimeStream("test content"));

        _mockEmlStorage.Setup(x => x.StoreEmlAsync(
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("eml/Inbox/2024/01/test.eml");

        _mockEmlStorage.Setup(x => x.GetFileSize(It.IsAny<string>()))
            .Returns(1024L);

        var options = new SyncOptions { MaxParallelDownloads = 1 }; // Sequential to ensure consistent ordering

        // Act
        var result = await _syncEngine.SyncAsync(options);

        // Assert
        result.Success.Should().BeTrue();
        result.MessagesSynced.Should().Be(2); // 2 successful
        result.Errors.Should().Be(1); // 1 failed
    }

    [Fact]
    public async Task SyncAsync_Cancellation_StopsGracefully()
    {
        // Arrange
        SetupBasicMocks();

        var folders = new List<AppMailFolder>
        {
            CreateTestFolder("folder-1", "Inbox", 100)
        };

        _mockGraphClient.Setup(x => x.GetFoldersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folders);

        var cts = new CancellationTokenSource();

        _mockGraphClient.Setup(x => x.GetMessagesDeltaAsync("folder-1", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                cts.Cancel(); // Cancel during folder processing
                return new DeltaQueryResult<MessageInfo>
                {
                    Items = new List<MessageInfo> { CreateTestMessageInfo("msg-1", "Test") },
                    HasMorePages = false
                };
            });

        var options = new SyncOptions();

        // Act
        var result = await _syncEngine.SyncAsync(options, cancellationToken: cts.Token);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cancelled");
    }

    #endregion

    #region Incremental Sync Tests

    [Fact]
    public async Task SyncAsync_WithStoredDeltaToken_UsesDeltaTokenForIncrementalSync()
    {
        // Arrange
        SetupBasicMocks();

        var folders = new List<AppMailFolder>
        {
            CreateTestFolder("folder-1", "Inbox", 1)
        };

        _mockGraphClient.Setup(x => x.GetFoldersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folders);

        // Setup stored folder with delta token
        var storedFolder = new Folder
        {
            GraphId = "folder-1",
            LocalPath = "Inbox",
            DisplayName = "Inbox",
            DeltaToken = "stored-delta-token-123",
            LastSyncTime = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-7),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        _mockDatabase.Setup(x => x.GetFolderAsync("folder-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedFolder);

        _mockGraphClient.Setup(x => x.GetMessagesDeltaAsync("folder-1", "stored-delta-token-123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltaQueryResult<MessageInfo>
            {
                Items = new List<MessageInfo> { CreateTestMessageInfo("msg-1", "New message") },
                HasMorePages = false,
                DeltaToken = "new-delta-token-456"
            });

        _mockDatabase.Setup(x => x.GetMessageByImmutableIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message?)null);

        SetupMimeDownloadMock();

        var options = new SyncOptions();

        // Act
        var result = await _syncEngine.SyncAsync(options);

        // Assert
        result.Success.Should().BeTrue();

        // Verify delta token was used
        _mockGraphClient.Verify(
            x => x.GetMessagesDeltaAsync("folder-1", "stored-delta-token-123", null, It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify folder was updated with new delta token
        _mockDatabase.Verify(
            x => x.UpsertFolderAsync(It.Is<Folder>(f =>
                f.GraphId == "folder-1" &&
                f.DeltaToken == "new-delta-token-456"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncAsync_DeltaTokenSaved_AfterSuccessfulSync()
    {
        // Arrange
        SetupBasicMocks();

        var folders = new List<AppMailFolder>
        {
            CreateTestFolder("folder-1", "Inbox", 1)
        };

        _mockGraphClient.Setup(x => x.GetFoldersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folders);

        // No stored folder (first sync)
        _mockDatabase.Setup(x => x.GetFolderAsync("folder-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Folder?)null);

        _mockGraphClient.Setup(x => x.GetMessagesDeltaAsync("folder-1", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltaQueryResult<MessageInfo>
            {
                Items = new List<MessageInfo> { CreateTestMessageInfo("msg-1", "Test") },
                HasMorePages = false,
                DeltaToken = "new-delta-token-abc"
            });

        _mockDatabase.Setup(x => x.GetMessageByImmutableIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message?)null);

        SetupMimeDownloadMock();

        var options = new SyncOptions();

        // Act
        var result = await _syncEngine.SyncAsync(options);

        // Assert
        result.Success.Should().BeTrue();

        // Verify folder was saved with delta token and last sync time
        _mockDatabase.Verify(
            x => x.UpsertFolderAsync(It.Is<Folder>(f =>
                f.GraphId == "folder-1" &&
                f.DeltaToken == "new-delta-token-abc" &&
                f.LastSyncTime.HasValue),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncAsync_FolderLastSyncTimeUpdated_AfterSync()
    {
        // Arrange
        SetupBasicMocks();

        var folders = new List<AppMailFolder>
        {
            CreateTestFolder("folder-1", "Inbox", 0)
        };

        _mockGraphClient.Setup(x => x.GetFoldersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folders);

        _mockDatabase.Setup(x => x.GetFolderAsync("folder-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Folder?)null);

        _mockGraphClient.Setup(x => x.GetMessagesDeltaAsync("folder-1", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltaQueryResult<MessageInfo>
            {
                Items = new List<MessageInfo>(),
                HasMorePages = false,
                DeltaToken = "delta-token"
            });

        var options = new SyncOptions();
        var beforeSync = DateTimeOffset.UtcNow;

        // Act
        var result = await _syncEngine.SyncAsync(options);

        // Assert
        result.Success.Should().BeTrue();

        _mockDatabase.Verify(
            x => x.UpsertFolderAsync(It.Is<Folder>(f =>
                f.LastSyncTime.HasValue &&
                f.LastSyncTime.Value >= beforeSync),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncAsync_MovedMessage_UpdatesDatabaseAndMovesFile()
    {
        // Arrange
        SetupBasicMocks();

        var folders = new List<AppMailFolder>
        {
            CreateTestFolder("folder-1", "Inbox", 0)
        };

        _mockGraphClient.Setup(x => x.GetFoldersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folders);

        // Setup stored folder
        _mockDatabase.Setup(x => x.GetFolderAsync("folder-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Folder?)null);

        // Return a moved message in delta results
        var movedMessage = new MessageInfo
        {
            Id = "msg-1",
            ImmutableId = "msg-1",
            Subject = "Moved message",
            From = "sender@example.com",
            ReceivedDateTime = DateTimeOffset.UtcNow.AddHours(-1),
            HasAttachments = false,
            IsDeleted = false,
            IsMoved = true,
            NewParentFolderId = "folder-2",
            ParentFolderId = "folder-2"
        };

        _mockGraphClient.Setup(x => x.GetMessagesDeltaAsync("folder-1", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltaQueryResult<MessageInfo>
            {
                Items = new List<MessageInfo> { movedMessage },
                HasMorePages = false,
                DeltaToken = "delta-token"
            });

        // Existing message in database
        var existingMessage = new Message
        {
            GraphId = "msg-1",
            ImmutableId = "msg-1",
            LocalPath = "eml/Inbox/2024/01/message.eml",
            FolderPath = "Inbox",
            Subject = "Moved message",
            ReceivedTime = DateTimeOffset.UtcNow.AddHours(-1),
            Size = 1024,
            HasAttachments = false,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        _mockDatabase.Setup(x => x.GetMessageByImmutableIdAsync("msg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMessage);

        // New folder exists in database
        var newFolder = new Folder
        {
            GraphId = "folder-2",
            LocalPath = "Archive",
            DisplayName = "Archive",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockDatabase.Setup(x => x.GetFolderAsync("folder-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(newFolder);

        // Setup move operation
        _mockEmlStorage.Setup(x => x.MoveEmlAsync(
            "eml/Inbox/2024/01/message.eml", "Archive", It.IsAny<CancellationToken>()))
            .ReturnsAsync("eml/Archive/2024/01/message.eml");

        var options = new SyncOptions();

        // Act
        var result = await _syncEngine.SyncAsync(options);

        // Assert
        result.Success.Should().BeTrue();

        // Verify file was moved
        _mockEmlStorage.Verify(
            x => x.MoveEmlAsync("eml/Inbox/2024/01/message.eml", "Archive", It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify database was updated
        _mockDatabase.Verify(
            x => x.UpdateMessageAsync(It.Is<Message>(m =>
                m.ImmutableId == "msg-1" &&
                m.LocalPath == "eml/Archive/2024/01/message.eml" &&
                m.FolderPath == "Archive"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncAsync_MovedMessage_DryRun_DoesNotMoveFile()
    {
        // Arrange
        SetupBasicMocks();

        var folders = new List<AppMailFolder>
        {
            CreateTestFolder("folder-1", "Inbox", 0)
        };

        _mockGraphClient.Setup(x => x.GetFoldersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folders);

        _mockDatabase.Setup(x => x.GetFolderAsync("folder-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Folder?)null);

        var movedMessage = new MessageInfo
        {
            Id = "msg-1",
            ImmutableId = "msg-1",
            Subject = "Moved message",
            From = "sender@example.com",
            ReceivedDateTime = DateTimeOffset.UtcNow.AddHours(-1),
            HasAttachments = false,
            IsDeleted = false,
            IsMoved = true,
            NewParentFolderId = "folder-2"
        };

        _mockGraphClient.Setup(x => x.GetMessagesDeltaAsync("folder-1", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltaQueryResult<MessageInfo>
            {
                Items = new List<MessageInfo> { movedMessage },
                HasMorePages = false
            });

        var existingMessage = CreateTestMessage("msg-1");
        _mockDatabase.Setup(x => x.GetMessageByImmutableIdAsync("msg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMessage);

        var newFolder = new Folder
        {
            GraphId = "folder-2",
            LocalPath = "Archive",
            DisplayName = "Archive",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockDatabase.Setup(x => x.GetFolderAsync("folder-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(newFolder);

        var options = new SyncOptions { DryRun = true };

        // Act
        var result = await _syncEngine.SyncAsync(options);

        // Assert
        result.Success.Should().BeTrue();

        // Verify file was NOT moved in dry run
        _mockEmlStorage.Verify(
            x => x.MoveEmlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Verify database was NOT updated in dry run
        _mockDatabase.Verify(
            x => x.UpdateMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Deletion Detection Tests

    [Fact]
    public async Task SyncAsync_DeletedMessage_QuarantinesMessageAndUpdatesDatabase()
    {
        // Arrange
        SetupBasicMocks();

        var folders = new List<AppMailFolder>
        {
            CreateTestFolder("folder-1", "Inbox", 0)
        };

        _mockGraphClient.Setup(x => x.GetFoldersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folders);

        _mockDatabase.Setup(x => x.GetFolderAsync("folder-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Folder?)null);

        // Return a deleted message in delta results
        var deletedMessage = new MessageInfo
        {
            Id = "msg-1",
            ImmutableId = "msg-1",
            Subject = "Deleted message",
            From = "sender@example.com",
            ReceivedDateTime = DateTimeOffset.UtcNow.AddHours(-1),
            HasAttachments = false,
            IsDeleted = true,
            IsMoved = false
        };

        _mockGraphClient.Setup(x => x.GetMessagesDeltaAsync("folder-1", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltaQueryResult<MessageInfo>
            {
                Items = new List<MessageInfo> { deletedMessage },
                HasMorePages = false,
                DeltaToken = "delta-token"
            });

        // Existing message in database
        var existingMessage = new Message
        {
            GraphId = "msg-1",
            ImmutableId = "msg-1",
            LocalPath = "eml/Inbox/2024/01/message.eml",
            FolderPath = "Inbox",
            Subject = "Deleted message",
            ReceivedTime = DateTimeOffset.UtcNow.AddHours(-1),
            Size = 1024,
            HasAttachments = false,
            QuarantinedAt = null,
            QuarantineReason = null,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        _mockDatabase.Setup(x => x.GetMessageByImmutableIdAsync("msg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMessage);

        // Setup quarantine operation
        _mockEmlStorage.Setup(x => x.MoveToQuarantineAsync(
            "eml/Inbox/2024/01/message.eml", It.IsAny<CancellationToken>()))
            .ReturnsAsync("_Quarantine/eml/Inbox/2024/01/message.eml");

        var options = new SyncOptions();

        // Act
        var result = await _syncEngine.SyncAsync(options);

        // Assert
        result.Success.Should().BeTrue();

        // Verify file was moved to quarantine
        _mockEmlStorage.Verify(
            x => x.MoveToQuarantineAsync("eml/Inbox/2024/01/message.eml", It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify database was updated with quarantine info
        _mockDatabase.Verify(
            x => x.UpdateMessageAsync(It.Is<Message>(m =>
                m.ImmutableId == "msg-1" &&
                m.LocalPath == "_Quarantine/eml/Inbox/2024/01/message.eml" &&
                m.QuarantinedAt != null &&
                m.QuarantineReason == "deleted_in_m365"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncAsync_DeletedMessage_DryRun_DoesNotQuarantine()
    {
        // Arrange
        SetupBasicMocks();

        var folders = new List<AppMailFolder>
        {
            CreateTestFolder("folder-1", "Inbox", 0)
        };

        _mockGraphClient.Setup(x => x.GetFoldersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folders);

        _mockDatabase.Setup(x => x.GetFolderAsync("folder-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Folder?)null);

        var deletedMessage = new MessageInfo
        {
            Id = "msg-1",
            ImmutableId = "msg-1",
            Subject = "Deleted message",
            From = "sender@example.com",
            ReceivedDateTime = DateTimeOffset.UtcNow.AddHours(-1),
            HasAttachments = false,
            IsDeleted = true,
            IsMoved = false
        };

        _mockGraphClient.Setup(x => x.GetMessagesDeltaAsync("folder-1", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltaQueryResult<MessageInfo>
            {
                Items = new List<MessageInfo> { deletedMessage },
                HasMorePages = false
            });

        var existingMessage = CreateTestMessage("msg-1");
        _mockDatabase.Setup(x => x.GetMessageByImmutableIdAsync("msg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMessage);

        var options = new SyncOptions { DryRun = true };

        // Act
        var result = await _syncEngine.SyncAsync(options);

        // Assert
        result.Success.Should().BeTrue();

        // Verify file was NOT moved to quarantine in dry run
        _mockEmlStorage.Verify(
            x => x.MoveToQuarantineAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Verify database was NOT updated in dry run
        _mockDatabase.Verify(
            x => x.UpdateMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncAsync_DeletedMessage_NotInDatabase_Skipped()
    {
        // Arrange
        SetupBasicMocks();

        var folders = new List<AppMailFolder>
        {
            CreateTestFolder("folder-1", "Inbox", 0)
        };

        _mockGraphClient.Setup(x => x.GetFoldersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folders);

        _mockDatabase.Setup(x => x.GetFolderAsync("folder-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Folder?)null);

        var deletedMessage = new MessageInfo
        {
            Id = "msg-1",
            ImmutableId = "msg-1",
            Subject = "Deleted message",
            From = "sender@example.com",
            ReceivedDateTime = DateTimeOffset.UtcNow.AddHours(-1),
            HasAttachments = false,
            IsDeleted = true,
            IsMoved = false
        };

        _mockGraphClient.Setup(x => x.GetMessagesDeltaAsync("folder-1", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltaQueryResult<MessageInfo>
            {
                Items = new List<MessageInfo> { deletedMessage },
                HasMorePages = false,
                DeltaToken = "delta-token"
            });

        // Message NOT in database
        _mockDatabase.Setup(x => x.GetMessageByImmutableIdAsync("msg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message?)null);
        _mockDatabase.Setup(x => x.GetMessageAsync("msg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message?)null);

        var options = new SyncOptions();

        // Act
        var result = await _syncEngine.SyncAsync(options);

        // Assert
        result.Success.Should().BeTrue();

        // Verify no quarantine operations
        _mockEmlStorage.Verify(
            x => x.MoveToQuarantineAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Verify no message updates
        _mockDatabase.Verify(
            x => x.UpdateMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncAsync_AlreadyQuarantinedMessage_Skipped()
    {
        // Arrange
        SetupBasicMocks();

        var folders = new List<AppMailFolder>
        {
            CreateTestFolder("folder-1", "Inbox", 0)
        };

        _mockGraphClient.Setup(x => x.GetFoldersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folders);

        _mockDatabase.Setup(x => x.GetFolderAsync("folder-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Folder?)null);

        var deletedMessage = new MessageInfo
        {
            Id = "msg-1",
            ImmutableId = "msg-1",
            Subject = "Already quarantined message",
            From = "sender@example.com",
            ReceivedDateTime = DateTimeOffset.UtcNow.AddHours(-1),
            HasAttachments = false,
            IsDeleted = true,
            IsMoved = false
        };

        _mockGraphClient.Setup(x => x.GetMessagesDeltaAsync("folder-1", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltaQueryResult<MessageInfo>
            {
                Items = new List<MessageInfo> { deletedMessage },
                HasMorePages = false,
                DeltaToken = "delta-token"
            });

        // Message already quarantined
        var alreadyQuarantined = new Message
        {
            GraphId = "msg-1",
            ImmutableId = "msg-1",
            LocalPath = "_Quarantine/eml/Inbox/2024/01/message.eml",
            FolderPath = "Inbox",
            Subject = "Already quarantined message",
            ReceivedTime = DateTimeOffset.UtcNow.AddHours(-1),
            Size = 1024,
            HasAttachments = false,
            QuarantinedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            QuarantineReason = "deleted_in_m365",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };

        _mockDatabase.Setup(x => x.GetMessageByImmutableIdAsync("msg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(alreadyQuarantined);

        var options = new SyncOptions();

        // Act
        var result = await _syncEngine.SyncAsync(options);

        // Assert
        result.Success.Should().BeTrue();

        // Verify no quarantine operations (already quarantined)
        _mockEmlStorage.Verify(
            x => x.MoveToQuarantineAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Verify no message updates
        _mockDatabase.Verify(
            x => x.UpdateMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Progress Callback Tests

    [Fact]
    public async Task SyncAsync_WithProgressCallback_ReportsProgress()
    {
        // Arrange
        SetupBasicMocks();

        var folders = new List<AppMailFolder>
        {
            CreateTestFolder("folder-1", "Inbox", 2)
        };

        _mockGraphClient.Setup(x => x.GetFoldersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folders);

        _mockGraphClient.Setup(x => x.GetMessagesDeltaAsync("folder-1", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeltaQueryResult<MessageInfo>
            {
                Items = new List<MessageInfo>
                {
                    CreateTestMessageInfo("msg-1", "Test 1"),
                    CreateTestMessageInfo("msg-2", "Test 2")
                },
                HasMorePages = false
            });

        _mockDatabase.Setup(x => x.GetMessageByImmutableIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message?)null);

        SetupMimeDownloadMock();

        var progressReports = new List<SyncProgress>();

        var options = new SyncOptions();

        // Act
        var result = await _syncEngine.SyncAsync(options, progress => progressReports.Add(progress));

        // Assert
        progressReports.Should().NotBeEmpty();
        progressReports.Should().Contain(p => p.Phase == "Enumerating folders");
        progressReports.Should().Contain(p => p.Phase == "Syncing folder");
        progressReports.Should().Contain(p => p.Phase == "Downloading messages");
    }

    #endregion

    #region Helper Methods

    private void SetupBasicMocks()
    {
        _mockGraphClient.Setup(x => x.GetUserEmailAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("user@example.com");

        _mockDatabase.Setup(x => x.GetSyncStateAsync("user@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);
    }

    private void SetupMimeDownloadMock()
    {
        _mockGraphClient.Setup(x => x.DownloadMessageMimeAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMimeStream("MIME content"));

        _mockEmlStorage.Setup(x => x.StoreEmlAsync(
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("eml/Inbox/2024/01/test.eml");

        _mockEmlStorage.Setup(x => x.GetFileSize(It.IsAny<string>()))
            .Returns(1024L);
    }

    private static MemoryStream CreateMimeStream(string content)
    {
        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
    }

    private static AppMailFolder CreateTestFolder(string id, string name, int itemCount)
    {
        return new AppMailFolder
        {
            Id = id,
            DisplayName = name,
            FullPath = name,
            TotalItemCount = itemCount,
            UnreadItemCount = 0
        };
    }

    private static MessageInfo CreateTestMessageInfo(string id, string subject)
    {
        return new MessageInfo
        {
            Id = id,
            ImmutableId = id,
            Subject = subject,
            From = "sender@example.com",
            ReceivedDateTime = DateTimeOffset.UtcNow.AddHours(-1),
            HasAttachments = false,
            IsDeleted = false
        };
    }

    private static Message CreateTestMessage(string graphId)
    {
        return new Message
        {
            GraphId = graphId,
            ImmutableId = graphId,
            LocalPath = "eml/Inbox/2024/01/test.eml",
            FolderPath = "Inbox",
            Subject = "Test",
            ReceivedTime = DateTimeOffset.UtcNow,
            Size = 1024,
            HasAttachments = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
