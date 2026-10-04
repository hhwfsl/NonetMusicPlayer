namespace NonetMusicPlayer.Core.Playback;

/// <summary>持久化数值与桌面旧版本一致，避免迁移时改变用户的循环方式。</summary>
public enum PlaybackMode { RepeatAll = 1, RepeatOne = 2, Shuffle = 3 }
