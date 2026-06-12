namespace TaxNetGuardian.Api;

public sealed record ExplanationClaim(
    string ClaimId,
    string Text,
    IReadOnlyList<string> EvidenceReferences,   // evidence item ids and/or citation chunk ids
    bool Grounded);                              // false => ungrounded indicator (AC 3,5)

public sealed record ValidatedClaim(
    ExplanationClaim Claim,
    bool Grounded);

public sealed record GuardrailOutcome(
    int GroundedCount, int UngroundedCount, int TotalClaimCount); // grounded+ungrounded==total (AC 4)

public sealed record ValidatedExplanation(
    string ExplanationId,
    string CaseId,
    string Summary,
    IReadOnlyList<ExplanationClaim> Claims,
    GuardrailOutcome Outcome,
    bool EvidenceBacked,                         // false when GroundedCount==0 (AC 8)
    string HumanReviewWarning);
