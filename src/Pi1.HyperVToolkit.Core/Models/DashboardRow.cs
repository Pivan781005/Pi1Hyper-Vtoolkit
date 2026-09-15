namespace Pi1.HyperVToolkit.Core.Models;

// Parity with Show-PiDashboard: EXACTLY 11 semantic rows in this order.
// Value stays a string because the reference mixes counts and formatted text.
public sealed record DashboardRow(
    string Area,
    string Status,
    string Value);
