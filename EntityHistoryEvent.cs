namespace ArchiveManager.Domain.Models;

/// <summary>A single chronological line for the "تاریخچه پرونده" view —
/// merges creation, transitions, and name-history rows into one timeline.</summary>
public sealed record EntityHistoryEvent(DateTime Date, string EventType, string Description);
