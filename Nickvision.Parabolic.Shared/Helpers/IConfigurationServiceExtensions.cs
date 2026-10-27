using Nickvision.Desktop.Application;
using Nickvision.Desktop.Filesystem;
using Nickvision.Parabolic.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nickvision.Parabolic.Shared.Helpers;

public static class IConfigurationServiceExtensions
{
    extension(IConfigurationService configurationService)
    {

        // ---- UniFetcher: organising and privacy (spec sections 6 and 7) ----
        public bool SubfolderPerSite
        {
            get => configurationService.Get("SubfolderPerSite", false);

            set => configurationService.Set("SubfolderPerSite", value);
        }

        /// <summary>UniFetcher: folder you chose for temporary/partial files; empty = next to the download (default).</summary>
        public string TempFolder
        {
            get => configurationService.Get("TempFolder", string.Empty);

            set => configurationService.Set("TempFolder", value);
        }

        /// <summary>UniFetcher: space to always keep free on the download / temp drive, in MB (0 = off). Default 2 GB.</summary>
        public int FreeSpaceLimitMb
        {
            get => configurationService.Get("FreeSpaceLimitMb", 2048);

            set => configurationService.Set("FreeSpaceLimitMb", value);
        }

        /// <summary>UniFetcher: skip (untick) videos you've already downloaded. Default on.</summary>
        public bool SkipAlreadyDownloaded
        {
            get => configurationService.Get("SkipAlreadyDownloaded", true);

            set => configurationService.Set("SkipAlreadyDownloaded", value);
        }

        public bool Incognito
        {
            get => configurationService.Get("Incognito", false);

            set => configurationService.Set("Incognito", value);
        }

        public NotificationMode NotificationMode
        {
            get => configurationService.Get("NotificationMode", NotificationMode.Normal);

            set => configurationService.Set("NotificationMode", value);
        }

        // ---- UniFetcher: yt-dlp / ffmpeg updates (spec section 2) ----
        public ToolUpdateMode ToolUpdateMode
        {
            get => configurationService.Get("ToolUpdateMode", ToolUpdateMode.Automatic);

            set => configurationService.Set("ToolUpdateMode", value);
        }

        public YtdlpChannel YtdlpChannel
        {
            get => configurationService.Get("YtdlpChannel", YtdlpChannel.Nightly);

            set => configurationService.Set("YtdlpChannel", value);
        }

        public string ToolsLastCheck
        {
            get => configurationService.Get("ToolsLastCheck", string.Empty);

            set => configurationService.Set("ToolsLastCheck", value);
        }

        public string YtdlpActiveVersion
        {
            get => configurationService.Get("YtdlpActiveVersion", "bundled");

            set => configurationService.Set("YtdlpActiveVersion", value);
        }

        public string YtdlpPreviousVersion
        {
            get => configurationService.Get("YtdlpPreviousVersion", string.Empty);

            set => configurationService.Set("YtdlpPreviousVersion", value);
        }

        public string YtdlpSkippedVersion
        {
            get => configurationService.Get("YtdlpSkippedVersion", string.Empty);

            set => configurationService.Set("YtdlpSkippedVersion", value);
        }

        public string FfmpegActiveVersion
        {
            get => configurationService.Get("FfmpegActiveVersion", "bundled");

            set => configurationService.Set("FfmpegActiveVersion", value);
        }

        public string FfmpegPreviousVersion
        {
            get => configurationService.Get("FfmpegPreviousVersion", string.Empty);

            set => configurationService.Set("FfmpegPreviousVersion", value);
        }

        public string Fat32WarnedDrives
        {
            get => configurationService.Get("Fat32WarnedDrives", string.Empty);

            set => configurationService.Set("Fat32WarnedDrives", value);
        }

        public string FfmpegInstalledAt
        {
            get => configurationService.Get("FfmpegInstalledAt", string.Empty);

            set => configurationService.Set("FfmpegInstalledAt", value);
        }
        public bool AllowPreviewUpdates
        {
            get => configurationService.Get("AllowPreviewUpdates", false);

            set => configurationService.Set("AllowPreviewUpdates", value);
        }

        public int AriaMaxConnectionsPerServer
        {
            get => configurationService.Get("AriaMaxConnectionsPerServer", 16);

            set => configurationService.Set("AriaMaxConnectionsPerServer", value);
        }

        public int AriaMinSplitSize
        {
            get => configurationService.Get("AriaMinSplitSize", 20);

            set => configurationService.Set("AriaMinSplitSize", value);
        }

        public Browser CookiesBrowser
        {
            get => configurationService.Get("CookiesBrowser", Browser.None);

            set => configurationService.Set("CookiesBrowser", value);
        }

        public string CookiesPath
        {
            get => configurationService.Get("CookiesPath", string.Empty);

            set => configurationService.Set("CookiesPath", value);
        }

        public bool CropAudioThumbnails
        {
            get => configurationService.Get("CropAudioThumbnails", false);

            set => configurationService.Set("CropAudioThumbnails", value);
        }

        public bool EmbedChapters
        {
            get => configurationService.Get("EmbedChapters", false);

            set => configurationService.Set("EmbedChapters", value);
        }

        public bool EmbedMetadata
        {
            get => configurationService.Get("EmbedMetadata", true);

            set => configurationService.Set("EmbedMetadata", value);
        }

        public bool EmbedSubtitles
        {
            get => configurationService.Get("EmbedSubtitles", true);

            set => configurationService.Set("EmbedSubtitles", value);
        }

        public bool EmbedThumbnails
        {
            get => configurationService.Get("EmbedThumbnails", true);

            set => configurationService.Set("EmbedThumbnails", value);
        }

        public HistoryLength HistoryLength
        {
            get => configurationService.Get("HistoryLength", HistoryLength.OneWeek);

            set => configurationService.Set("HistoryLength", value);
        }

        public bool HistorySortNewest
        {
            get => configurationService.Get("HistorySortNewestFirst", true);

            set => configurationService.Set("HistorySortNewestFirst", value);
        }

        public bool IncludeAutoGeneratedSubtitles
        {
            get => configurationService.Get("IncludeAutoGeneratedSubtitles", true);

            set => configurationService.Set("IncludeAutoGeneratedSubtitles", value);
        }

        public bool IncludeMediaIdInTitle
        {
            get => configurationService.Get("IncludeMediaIdInTitle", true);

            set => configurationService.Set("IncludeMediaIdInTitle", value);
        }

        public bool IncludeSuperResolutions
        {
            get => configurationService.Get("IncludeSuperResolutions", true);

            set => configurationService.Set("IncludeSuperResolutions", value);
        }

        public bool LimitCharacters
        {
            get => configurationService.Get("LimitCharacters", OperatingSystem.IsWindows());

            set => configurationService.Set("LimitCharacters", value);
        }

        public int MaxNumberOfActiveDownloads
        {
            get => Math.Clamp(configurationService.Get("MaxNumberOfActiveDownloads", 2), 1, 5);

            set => configurationService.Set("MaxNumberOfActiveDownloads", value);
        }

        public bool OverwriteExistingFiles
        {
            // UniFetcher never overwrites existing files (spec section 8); new files get a numbered name instead
            get => false;

            set => configurationService.Set("OverwriteExistingFiles", value);
        }

        public int PostprocessingThreads
        {
            get => configurationService.Get("PostprocessingThreads", Environment.ProcessorCount);

            set => configurationService.Set("PostprocessingThreads", value);
        }

        public double PreviousAudioBitrate
        {
            get => configurationService.Get("AudioBitrate", double.MaxValue);

            set => configurationService.Set("AudioBitrate", value);
        }

        public Dictionary<MediaFileType, string> PreviousAudioFormatIds
        {
            get => configurationService.Get("AudioFormatIds", new()
            {
                { MediaFileType.Video, Format.BestAudio.Id },
                { MediaFileType.MP4, Format.BestAudio.Id },
                { MediaFileType.MKV, Format.BestAudio.Id },
                { MediaFileType.WEBM, Format.BestAudio.Id },
                { MediaFileType.MOV, Format.BestAudio.Id },
                { MediaFileType.AVI, Format.BestAudio.Id },
                { MediaFileType.Audio, Format.BestAudio.Id },
                { MediaFileType.MP3, Format.BestAudio.Id },
                { MediaFileType.M4A, Format.BestAudio.Id },
                { MediaFileType.OPUS, Format.BestAudio.Id },
                { MediaFileType.FLAC, Format.BestAudio.Id },
                { MediaFileType.WAV, Format.BestAudio.Id },
                { MediaFileType.OGG, Format.BestAudio.Id },
            }, ApplicationJsonContext.Default.DictionaryMediaFileTypeString);

            set => configurationService.Set("AudioFormatIds", value, ApplicationJsonContext.Default.DictionaryMediaFileTypeString);
        }

        public MediaFileType PreviousAudioOnlyFileType
        {
            get => configurationService.Get("AudioOnlyFileType", MediaFileType.MP3);

            set => configurationService.Set("AudioOnlyFileType", value);
        }

        public bool PreviousDownloadImmediatelyAsAudio
        {
            get => configurationService.Get("DownloadImmediatelyAsAudio", false);

            set => configurationService.Set("DownloadImmediatelyAsAudio", value);
        }

        public bool PreviousDownloadImmediatelyAsVideo
        {
            get => configurationService.Get("DownloadImmediatelyAsVideo", false);

            set => configurationService.Set("DownloadImmediatelyAsVideo", value);
        }

        public bool PreviousExportDescription
        {
            get => configurationService.Get("ExportDescription", false);

            set => configurationService.Set("ExportDescription", value);
        }

        public bool PreviousExportM3U
        {
            get => configurationService.Get("ExportM3U", false);

            set => configurationService.Set("ExportM3U", value);
        }

        public MediaFileType PreviousFullFileType
        {
            get => configurationService.Get("FullFileType", MediaFileType.MP4);

            set => configurationService.Set("FullFileType", value);
        }

        public bool PreviousNumberTitles
        {
            get => configurationService.Get("NumberTitles", false);

            set => configurationService.Set("NumberTitles", value);
        }

        public string PreviousPostProcessorArgumentName
        {
            get => configurationService.Get("PostProcessorArgumentName", string.Empty);

            set => configurationService.Set("PostProcessorArgumentName", value);
        }

        public bool PreviousReverseDownloadOrder
        {
            get => configurationService.Get("ReverseDownloadOrder", false);

            set => configurationService.Set("ReverseDownloadOrder", value);
        }

        public string PreviousSaveFolder
        {
            get
            {
                var saveFolder = configurationService.Get("SaveFolder", string.Empty);
                if (string.IsNullOrEmpty(saveFolder))
                {
                    // UniFetcher default: a "UniFetcher" folder in Downloads
                    saveFolder = Path.Combine(UserDirectories.Downloads, "UniFetcher");
                    try
                    {
                        Directory.CreateDirectory(saveFolder);
                    }
                    catch
                    {
                        saveFolder = UserDirectories.Downloads;
                    }
                    return saveFolder;
                }
                if (Directory.Exists(saveFolder))
                {
                    return saveFolder;
                }
                // UniFetcher: never silently fall back to the internal drive (spec 6a).
                // If the drive came back under another letter, follow it; otherwise keep the chosen folder and let the queue pause.
                var driveId = configurationService.Get("SaveFolderDriveId", string.Empty);
                var oldRoot = Path.GetPathRoot(saveFolder) ?? string.Empty;
                if (!string.IsNullOrEmpty(driveId) && !string.IsNullOrEmpty(oldRoot))
                {
                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        try
                        {
                            if (drive.IsReady && Services.QueueGuardService.GetVolumeSerial(drive.RootDirectory.FullName) == driveId)
                            {
                                var moved = drive.RootDirectory.FullName + saveFolder[oldRoot.Length..];
                                configurationService.Set("SaveFolder", moved);
                                return moved;
                            }
                        }
                        catch
                        {
                            // drive went away while looking
                        }
                    }
                }
                return saveFolder;
            }

            set
            {
                configurationService.Set("SaveFolder", value);
                var root = Path.GetPathRoot(value);
                configurationService.Set("SaveFolderDriveId", string.IsNullOrEmpty(root) ? string.Empty : Services.QueueGuardService.GetVolumeSerial(root) ?? string.Empty);
            }
        }

        public bool PreviousSplitChapters
        {
            get => configurationService.Get("SplitChapters", false);

            set => configurationService.Set("SplitChapters", value);
        }

        public IReadOnlyList<SubtitleLanguage> PreviousSubtitleLanguages
        {
            get => configurationService.Get("SubtitleLanguages", [], ApplicationJsonContext.Default.ListSubtitleLanguage);

            set => configurationService.Set("SubtitleLanguages", value.ToList(), ApplicationJsonContext.Default.ListSubtitleLanguage);
        }

        public Dictionary<MediaFileType, string> PreviousVideoFormatIds
        {
            get => configurationService.Get("VideoFormatIds", new()
            {
                { MediaFileType.Video, Format.SmartVideo.Id },
                { MediaFileType.MP4, Format.SmartVideo.Id },
                { MediaFileType.MKV, Format.SmartVideo.Id },
                { MediaFileType.WEBM, Format.SmartVideo.Id },
                { MediaFileType.MOV, Format.SmartVideo.Id },
                { MediaFileType.AVI, Format.SmartVideo.Id },
                { MediaFileType.Audio, Format.NoneVideo.Id },
                { MediaFileType.MP3, Format.NoneVideo.Id },
                { MediaFileType.M4A, Format.NoneVideo.Id },
                { MediaFileType.OPUS, Format.NoneVideo.Id },
                { MediaFileType.FLAC, Format.NoneVideo.Id },
                { MediaFileType.WAV, Format.NoneVideo.Id },
                { MediaFileType.OGG, Format.NoneVideo.Id },
            }, ApplicationJsonContext.Default.DictionaryMediaFileTypeString);

            set => configurationService.Set("VideoFormatIds", value, ApplicationJsonContext.Default.DictionaryMediaFileTypeString);
        }

        public MediaFileType PreviousVideoOnlyFileType
        {
            get => configurationService.Get("VideoOnlyFileType", MediaFileType.MP4);

            set => configurationService.Set("VideoOnlyFileType", value);
        }

        public VideoResolution PreviousVideoResolution
        {
            get => configurationService.Get("VideoResolution", VideoResolution.Best, ApplicationJsonContext.Default.VideoResolution);

            set => configurationService.Set("VideoResolution", value, ApplicationJsonContext.Default.VideoResolution);
        }

        public List<PostProcessorArgument> PostprocessingArguments
        {
            get => configurationService.Get("PostprocessingArguments", [], ApplicationJsonContext.Default.ListPostProcessorArgument);

            set => configurationService.Set("PostprocessingArguments", value, ApplicationJsonContext.Default.ListPostProcessorArgument);
        }

        public AudioCodec PreferredAudioCodec
        {
            get => configurationService.Get("PreferredAudioCodec", AudioCodec.Any);

            set => configurationService.Set("PreferredAudioCodec", value);
        }

        public FrameRate PreferredFrameRate
        {
            get => configurationService.Get("PreferredFrameRate", FrameRate.Any);

            set => configurationService.Set("PreferredFrameRate", value);
        }

        public SubtitleFormat PreferredSubtitleFormat
        {
            get => configurationService.Get("PreferredSubtitleFormat", SubtitleFormat.Any);

            set => configurationService.Set("PreferredSubtitleFormat", value);
        }

        public VideoCodec PreferredVideoCodec
        {
            get => configurationService.Get("PreferredVideoCodec", VideoCodec.Any);

            set => configurationService.Set("PreferredVideoCodec", value);
        }

        public bool PreventSuspend
        {
            get => configurationService.Get("PreventSuspend", false);

            set => configurationService.Set("PreventSuspend", value);
        }

        public string ProxyUrl
        {
            get => configurationService.Get("ProxyUrl", string.Empty);

            set => configurationService.Set("ProxyUrl", value);
        }

        public bool RemoveSourceData
        {
            get => configurationService.Get("RemoveSourceData", false);

            set => configurationService.Set("RemoveSourceData", value);
        }

        public bool ShowDisclaimerOnStartup
        {
            get => configurationService.Get("ShowDisclaimerOnStartup", true);

            set => configurationService.Set("ShowDisclaimerOnStartup", value);
        }

        public int? SpeedLimit
        {
            get
            {
                var limit = configurationService.Get("SpeedLimit", string.Empty);
                if (int.TryParse(limit, out var result))
                {
                    return result;
                }
                else
                {
                    return null;
                }
            }

            set => configurationService.Set("SpeedLimit", value.HasValue ? value.Value.ToString() : string.Empty);
        }

        public Theme Theme
        {
            get => configurationService.Get("Theme", Theme.System);

            set => configurationService.Set("Theme", value);
        }

        public bool TranslateMetadataAndChapters
        {
            get => configurationService.Get("TranslateMetadataAndChapters", true);

            set => configurationService.Set("TranslateMetadataAndChapters", value);
        }

        public string TranslationLanguage
        {
            get => configurationService.Get("TranslationLanguage", string.Empty);

            set => configurationService.Set("TranslationLanguage", value);
        }

        public bool UseAria
        {
            // aria2c is not bundled with UniFetcher (no official Windows build source), so it is always off
            get => false;

            set => configurationService.Set("UseAria", value);
        }

        public bool UsePartFiles
        {
            get => configurationService.Get("UsePartFiles", true);

            set => configurationService.Set("UsePartFiles", value);
        }

        public WindowGeometry WindowGeometry
        {
            get => configurationService.Get("WindowGeometry", new WindowGeometry(), ApplicationJsonContext.Default.WindowGeometry);

            set => configurationService.Set("WindowGeometry", value, ApplicationJsonContext.Default.WindowGeometry);
        }

        public bool YouTubeSponsorBlock
        {
            get => configurationService.Get("YouTubeSponsorBlock", false);

            set => configurationService.Set("YouTubeSponsorBlock", value);
        }

        public string YtdlpDiscoveryArgs
        {
            get => configurationService.Get("YtdlpDiscoveryArgs", string.Empty);

            set => configurationService.Set("YtdlpDiscoveryArgs", value);
        }

        public string YtdlpDownloadArgs
        {
            get => configurationService.Get("YtdlpDownloadArgs", string.Empty);

            set => configurationService.Set("YtdlpDownloadArgs", value);
        }

        public async Task<string> ToStringAsync()
        {
            var res = await configurationService.GetAllRawAsync();
            var builder = new StringBuilder();
            foreach (var pair in res.ToImmutableSortedDictionary())
            {
                builder.AppendLine($"{pair.Key} = {(string.IsNullOrEmpty(pair.Value) ? "\"\"" : pair.Value)}");
            }
            return builder.ToString();
        }
    }
}
