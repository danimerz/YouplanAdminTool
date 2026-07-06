namespace YouplanAdminTool.Core.Models;

/// <summary>Ein frisch erkannter Handlungsbedarf: dieser Antrag braucht die angegebene SAP-Aktion.</summary>
public sealed record AbsenceActionItem(AbsenceRequest Request, SapAction Action);
