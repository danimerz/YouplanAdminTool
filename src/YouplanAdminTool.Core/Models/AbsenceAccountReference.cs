namespace YouplanAdminTool.Core.Models;

/// <summary>Konto, über das ein Abwesenheitsantrag gebucht wurde (z.B. "Urlaub", "Gleitzeit").</summary>
public sealed record AbsenceAccountReference(long AccountId, string Name, AbsenceType AbsenceType);
