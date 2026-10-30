namespace GraduateRecords.Application.Common.Security;

/// <summary>
/// Phase 1 student-data scope (ADR-0017 StaffScope rule). StaffScope is an
/// authorization/data-access boundary, never an authentication category
/// (ADR-0017 §1): GraduateStaff ⇒ <see cref="AllStudents"/>;
/// Student ⇒ <see cref="None"/>; Administrator ⇒ <see cref="None"/> — the
/// Administrator role never grants student-data scope (ADR-0017 §12).
/// No per-department, per-faculty or per-staff scope exists in Phase 1
/// (ADR-0004 §4.1, preserved by ADR-0017).
/// </summary>
public enum StaffScope
{
    /// <summary>No student-data access (fail-closed value for every non-GraduateStaff holder).</summary>
    None = 0,

    /// <summary>Covers every student record (Phase 1: ADR-0004 §4.1).</summary>
    AllStudents = 1,
}
