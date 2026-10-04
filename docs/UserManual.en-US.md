# Nonet User Manual

Desktop version 0.4.0-beta.1. Chinese, Japanese and English interfaces provide the same features.

Hover over a button to see its full name and any existing action guidance. Hints follow the current language and button state. Ellipsized text, read-only paths and selections also show their full content; long hints wrap without requiring a wider window.

## Getting started

Keep the complete platform folder. On Windows 10 22H2 or Windows 11 x64, run Nonet.exe. macOS and Linux builds are prereleases awaiting platform testing. The executable directory must be writable; generated data defaults to its Data folder.

Select or create a playlist, then use its Add Music / Add Folder icons (with tooltips), or drop files into that playlist. Folder import includes subfolders. Dropping files on the player bar or opening audio through the OS starts temporary playback without importing it into playlists. Use Favorite or Add to Playlist to keep it. Original music stays in place. Tags and artwork are read during import; matching song.lrc / song.txt files are copied to the separate lyrics directory.

## Music home and playback

Music is a compact home with album and artist cards. Browse Songs shows the union of songs referenced by playlists; View All opens each full category. Album and artist links no longer occupy the sidebar. Minimizing/restoring or regaining focus preserves the current scroll position. Explicitly opening songs/recent starts at the top; only the playlist that owns playback locates its playing song. Deleting another playlist from the sidebar does not leave the current page.

Shuffle reduces the probability of previously selected songs without excluding them. With at least two distinct songs, the next random choice cannot be the current song. Weight history is session-local and does not change playlist ordering.

## Player terminal and independent CLI

The Terminal sits between Plugins and Settings. Enter commands after `nonet $ `; the `nonet` prefix is optional within a session, such as `nonet help` and `nonet player pause`. `nonet clear` clears display only; `nonet exit` returns to Home. Mouse, keyboard, plugins and commands share player state. Terminal logs are the exact records persisted to Logs; typed commands are not log entries. Confirm with y + Enter or `-y`, cancel with `-n`. Settings → Terminal provides opacity, font size, scrollback (1,000 by default, up to 10,000) and a minimum displayed log level. Filtering does not change disk logging. System shell commands are not executed.

The standalone CLI executable is `nonet`, in `publish/cli`. Add its full release directory to user PATH to launch it from CMD, PowerShell, Linux or macOS terminals. It has no graphical dependency and uses independent data/playback. Playback still needs local audio services. PgUp reviews the application's bounded scrollback; the outer terminal controls its own buffer and font. Never use Desktop Data for CLI. Use `--stay` to keep playback after a command. See [CLI guide](CLI.md) and [command reference](COMMANDS.md).

The optional precise lyric timing plugin contributes an item to Lyrics → More and a sidebar page. Choose a song (the current one is the default) and a lyric file, then start from the beginning, and press Space for each next line; line one is zero. Undo is available. Saving complete annotations creates and links an LRC without modifying the source file. Song changes/mode changes are locked while annotating; track end pauses. Cancel, navigation or disabling the plugin discards unsaved work and releases the restriction.

## Song playback

Search titles, artists, albums and providers. Enter, the magnifier or leaving the input applies the search; the cross clears it. Double-click a song or use its Play button. The player provides previous, play/pause, next, repeat modes, favorites, more actions, volume and desktop lyrics.

The seek bar spans the top edge of the player. Click or drag to seek. Modes are repeat list, repeat one and shuffle. A red heart means the song belongs to the permanent Favorites playlist; click again to remove it. Volume and last playback position are saved, but startup does not autoplay.

An unplayable song shows an error and advances to the next candidate. Playback stops if every song in the current list fails. Check the file path, encoding, output device and, for server tracks, the provider configuration. Windows taskbar previews offer previous, play/pause and next; this feature is Windows-only.

Changing or losing audio output pauses playback, retains the track and position, and selects an available device. Press Play to continue; reconnect a device first if none are available. Output failures do not mark a song as unreadable. Only one instance runs per user across installation folders. Opening another audio file activates that instance and replaces its current temporary playback.

Files dropped on the player bar always play temporarily, even while a playlist is open. Metadata, artwork and lyrics are read. Temporary audio enters recent history, but not the library, album/artist summaries or statistics. Favorite or Add to Playlist starts normal recording from that point. No temporary-playback page is opened.

## Playlists, albums and artists

Favorites cannot be deleted or renamed, but its cover and description can change. Other playlists can be created, renamed, edited, exported/imported as M3U and deleted without deleting music files. New additions appear first. Drag songs to reorder a playlist and drag sidebar playlists to reorder the sidebar. Column headings do not sort playlists.

The playing highlight belongs only to the source playlist. Changing songs or returning to that playlist locates the playing song. Clicking a row does not select it. Enter Batch Actions to reveal song checkboxes and one tri-state Select All checkbox: unchecked, partly selected, or all selected. Click the checkbox to select/clear the entire list. Leaving batch mode clears selections.

Library, Recent History and non-source playlists start at the top each time they are opened. The same song appearing in another playlist does not cause that playlist to scroll.

Songs and playlists have context menus; touch users can use visible More buttons. Favorite songs show Remove from Favorites. Song information includes artwork, tags and the complete path, with selectable, copyable, read-only text. A separate Windows action opens the folder and selects the file. Albums and artists use cover tiles with options to replace or reset artwork.

Cover selection opens a crop preview: drag the picture, zoom with the wheel or slider, or enter exact X/Y/side pixel values. Confirm to create a 320 × 320 managed cover; cancel keeps the current cover. Background and cover images are limited to 20 MB and checked for excessive pixel dimensions before decoding.

## Lyrics

Click the player artwork to open a full-width lyric view with artwork on the left and lyrics on the right. Click again to return. Timed lyrics scroll automatically. Each wheel step previews one adjacent line; dragging with a mouse or touch snaps to the nearest centered line when released. Scrollbars are hidden. After scrolling stops, a Play button beside the centered line seeks to that line. Reopening the view restores the current playback line. Double-clicking lyrics does not seek. Preview leaves the playback-line highlight unchanged and returns to following after five idle seconds. Long lines wrap inside one timestamped row. More offers LRC/TXT import and Show Lyric File in Folder (Windows selects the file). Plain untimed text cannot provide precise line seeking.

Desktop Lyrics is an independent window: minimizing or hiding the main player does not hide lyrics. The player button reflects the restored visibility and lock state. Both lines follow the theme color without displaced shadows. Hover text to reveal the frame; leaving hides it. Blank areas pass clicks through when the frame is hidden. While unlocked, drag the four edges to resize or drag elsewhere except buttons to move; position and dimensions are saved on release. Mouse and touch are supported. Settings → Lyrics → Desktop lyrics font size defaults to 28 px (14–48 px), applied on Enter or loss of focus. Translation text is always 68.75% of the primary size. Without a translation, only the current original line is shown, never the next line. Resizing and changing line length never rescale fonts. Long lines scroll horizontally inside the fixed window instead of automatically expanding it.

Choose a year and month directly from the statistics calendar dropdowns. The non-editable year list covers the current year minus 100 through the current year plus 100 (201 choices) and supports scrolling and keyboard selection. Dates cannot exceed these bounds.

Artist and album context menus and detail menus distinguish **Restore default cover**, which finds parsed song artwork, from **Use application default cover**, which forces the missing-artwork placeholder. A coverless first song does not hide artwork from other songs in the group. These choices persist across restarts.

Lock from the lyric frame to prevent moving, selecting or closing it and pass all clicks through. The player-bar lyric button becomes Unlock; press it to unlock without hiding lyrics, then close if desired. Linux click-through requires X11 Shape; unsupported systems may show lyrics without full input transparency.

Equal timestamps group the first line as the original and subsequent distinct lines as translations. Blank rows are omitted and translation text is slightly smaller than the original. This is a common bilingual-file convention, not language detection. Multiple timestamps and millisecond offset tags are supported; hovering does not highlight the lyric background.

LRC accepts `[mm:ss]`, `[mm:ss.xx]`, `[mm:ss.xxx]`, `[mm:ss:xx]` and `[mm:ss;xx]`. Fractions are interpreted by their digit count: `.12`, `:12` and `;12` mean 120 milliseconds, while `.123` means 123 milliseconds. These formats also work with word timestamps and offset tags.

## Settings and appearance

Language changes apply immediately. The default theme follows the system; Light and Dark are also available. The theme color applies to selection and interaction states. Select a color with the wheel, RGB fields or hexadecimal input. Background image and available control styles provide further customization.

JSON layouts can adjust supported regions and necessary player controls. Export a working layout first, edit it in your own external editor, save, then validate and reload in Settings. Settings does not display the JSON contents. Invalid layouts preserve the current layout. Reset defaults or restore a valid backup if needed. Layouts cannot run scripts or remove required playback controls; see UI_LAYOUT.md for the schema.

On Windows, window dots can be left or right. macOS defaults to left; Windows/Linux default to right, with existing preferences retained. Right order: minimize, maximize/restore, close. Left order: close, minimize, maximize/restore, with the logo, app name and version centered. Colors continue to identify the same functions. My Music and Plugins sections can collapse; playlists can also collapse with the button next to New Playlist; the state is saved. Plugins is hidden when no plugin pages are available.

Search Settings to locate an option; all sections remain expanded. Blank areas also respond to wheel scrolling. The manual button beside the Settings title opens this manual. Click path entries to open their directories. Reset Settings requires confirmation and retains music, playlists, plugins, statistics and storage directories.

Drag the navigation boundary to change its width. Use the short central grip below the seek bar and above playback controls to change player height; dragging the seek bar never resizes the player. Both support mouse and touch and save their sizes. Resizing resets custom workspace coordinates to adjacent regions but keeps nested controls and a backup. Speed and equalizer controls have been removed.

The initial window fits the monitor's logical work area, including portrait and high-DPI screens. Touch and pen can drag the custom title bar, including restoring a maximized window. Touch target sizing adapts automatically; no manual touch optimization switch is provided.

Background images cover the whole window. Separate title, navigation, content and player opacity controls change only their background overlays, not text or buttons; lower values reveal more of the image. Window opacity still affects the entire app. Select an installed font or import a TTF/OTF/TTC file up to 32 MB for immediate use with character fallback. A managed copy is kept in Data/Fonts, without installing the font into the OS. Choosing Default Font restores the original font.

Playback settings retain the latest 1,000 tracks by default. Set the history limit to zero to disable it. Lowering the limit removes older history entries, not songs or listening statistics.

Text and number inputs apply on Enter or losing focus, including clicks in blank areas. Invalid input leaves the last applied value unchanged. Multiline fields use Ctrl+Enter or leaving the field; Enter inserts a line break. Toggles, choices and sliders apply immediately. Plugin form edits are drafts until Save and Close.

Minimize Memory Optimization is on by default: it only collects unused objects, retaining current pages, artwork and scroll state, without forcibly trimming the process working set. No fixed memory ceiling is promised. Close defaults to the system tray; choose direct exit in Settings if preferred. The tray menu contains only Open Application and Exit Application; Exit fully quits. If the system tray is unavailable, closing exits normally.

Windows: register audio support in Settings, then choose Nonet in system Default Apps. Existing defaults are not overwritten automatically. Register again after moving the app. macOS: open the app, then use Finder Get Info → Open With → Change All. Linux: register the audio handler in Settings (xdg-utils required), then choose it in the file manager. OS registration writes an OS-maintained application entry; player data remains in its configured folder. macOS/Linux integration awaits platform testing.

Shortcuts can be recorded, disabled, reset and saved immediately. Conflicts and invalid combinations prevent saving. Common defaults: Space play/pause, Ctrl+Left/Right previous/next, Ctrl+D favorite, Ctrl+O add music, Ctrl+L search and Ctrl+B batch mode. Inputs, sliders, selection controls and plugin games retain their own keys. Touch operation is supported through visible buttons and menus.

## Listening statistics

The top four cards retain overall totals. A shared calendar selection controls daily, monthly and yearly panels for that date, its month and its year. Each shows total duration, play count and longest-listened / most-played songs and artists. Pausing, loading, seeking and unsaved temporary playback do not add listening time. Statistics stay local.

## Plugins

Select or drag an .impp file into Plugin Center, review the author, type and permissions, then enable its switch. Only .impp packages are accepted. GitHub Release Import accepts public release-tag, latest or .impp asset URLs; choose a platform asset if several exist. It downloads assets up to 128 MiB, not repository source code. Cards show basic metadata; the trash button disables before uninstalling. New installations are disabled by default. Enabled page plugins appear in the sidebar Plugins section.

Download progress appears in a toast at the top right of the main window, not in a separate window. The close icon cancels the download. Known sizes show a percentage; unknown sizes show a busy indicator. After download and verification, the progress toast disappears and a separate completion toast closes automatically after about four seconds. Download completion does not install or enable the plugin: review its information and permissions first.

Games stop their timers when their page closes. Declarative pets may remain in a floating window until disabled, uninstalled or the player exits; their timers and subscriptions are then released. The snake sample uses arrows/WASD, Space to pause and R to restart. The music-cat sample can control playback, favorites and search, and display track-change messages.

The configuration icon opens an in-app form generated from plugin_config_schema.json, with descriptions, toggles, choices, numbers and lists. Individual reset buttons restore defaults. Save and Close validates and applies; Close discards drafts. Only the developer schema defines the form. Missing or empty schemas show No configuration; users cannot add, remove or rename configuration fields. Provider More can also load its catalog. Providers communicate with an existing server; the player does not create a server. Password and sensitive authentication fields are session-only and must be entered again after restart. Native provider processes are not sandboxed; install only trusted plugins. PLUGIN_DEVELOPMENT.md describes interfaces and limitations.

Background image opacity defaults to 50%; existing choices are retained. Background selection uses the same draggable crop preview as covers, with an application or original-image aspect ratio. The preview is on the left and exact parameters and Save are on the right, without scrolling the form. The right-hand navigation divider stays visible; a top-mounted player's resize grip moves to its lower edge.

The Windows installer is in publish/desktop/windows_installer. It installs per user without administrator privileges by default and requires a writable destination. Upgrading and uninstalling preserve the Data folder and directory-choice file. Uninstall removes the application, not original music or externally configured data. Back up important data separately.

## Data, backups and recovery

Data contains references, playlists, settings, progress, statistics, covers, lyrics, plugins, layouts and logs. Nonet.bootstrap.json beside the executable records custom directory choices. Music files are not included in state backups.

After applying a background, the managed Artwork/Backgrounds folder retains only the active image and up to two recent copies. A full application exit retains only the active image; without an active background, old managed image copies are removed. Original images, covers, other folders and non-image files are not removed. Hiding to the tray is not a full exit.

Settings can change data and backup directories. Migration retains the original data and explains when the new directory takes effect; do not force the process to exit during copying. Follow the restart prompt when a directory change requires it. Recovery accepts a valid backup or an older state.json, archives the current state first and does not autoplay or delete music files.

Back up the full Data folder separately from original music and external lyric folders. Moving the player does not move externally referenced music. If a path changes, make it accessible or reimport.

## Troubleshooting

- Missing or unplayable music: check paths, permissions, devices, network and provider settings.
- Missing artwork: the file may have no embedded/adjacent artwork; customize playlist, album or artist covers.
- Unsynchronized lyrics: import timed LRC and check the timing offset.
- Invalid layout: restore a valid layout or defaults without clearing the library.
- Plugin failures: check the package version, platform entry, permissions and configuration.
- Diagnostics: bounded, rotating logs are in Data/Logs. Common credential fields are redacted; review logs for private information before sharing.

## Lyrics, data and updates

Inline `[time]` and `<time>` enhanced LRC fills each glyph from left to right in both lyric views, without whole-character opacity fading or displaced desktop shadows. Translation uses its own timestamps when present, otherwise the original line's completion ratio. Ordinary LRC keeps line highlighting. Hovering or dragging the playback slider previews the pointer target in minutes:seconds; hovering never seeks, and leaving hides the preview. Regaining focus immediately restores the current lyric position. Embedded lyrics are read directly from tags without exporting a separate file; manual lyric selection takes precedence.

Recent removal only changes history. Albums and artists are read-only classifications. Removing the last playlist reference cleans application-generated metadata, artwork and lyric copies, never source audio or source lyrics. Confirmed Browse Songs deletion removes every playlist reference. M3U names use a translated `Playlist_<name>` prefix.

Desktop state uses Data/library.db (SQLite), with incremental progress saves and consistent backups. Existing state.json is migrated but retained. CLI uses a separate source-generated JSON state and defaults to a character TUI. Tab / Shift+Tab switches pages, arrows select, Enter opens or executes, and Space toggles playback; F1–F7 are not used. Commands are entered only in Terminal, between Plugins and Help: Up / Down recalls history, PageUp / PageDown scrolls output, and Ctrl+L clears it. Settings has keyboard-editable fields. Ctrl+K opens the complete action picker and parameter forms; Tab selects fields, Ctrl+N adds a repeated argument, Enter submits, and Esc cancels. Destructive confirmation defaults to cancel; press Y to approve. Ctrl+D exits. See CLI.md for the full keyboard reference. Use `--repl` for a scrolling prompt or `--no-tui` for line mode.

About is the last Settings section, with version, Check for Updates and repository link. Startup also checks in the background. A New badge opens release notes, download progress and Restart to Update. Application files are replaced after exit and rolled back on replacement failure; data is excluded. The configured repository is https://github.com/hhwfsl/NonetMusicPlayer; a missing repository or compatible release simply provides no update. A published executable and writable installation folder are required.

Uninstalling a plugin disables it first and asks whether to delete files or retain them under Plugins/Retained. Providers may declare disable, uninstall and shutdown callbacks; the host disposes declarative page resources.

This is prerelease software. Keep backups and avoid untrusted native process plugins.

Back to Top is an icon button after Clear Search in Settings and after Batch Selection in track, recent, album and artist lists. It changes only the viewport, not playback or selected tracks.
