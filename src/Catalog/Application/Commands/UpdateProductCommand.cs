namespace Portfolio.Catalog.Application;

/// <summary>
/// Request to rename a product or change its description (BR-CAT-001), with JSON Merge Patch semantics: a member the
/// request did not name is left unchanged, and a member named with <c>null</c> is cleared (which only a description allows).
/// </summary>
/// <param name="ProductId">Product identifier.</param>
/// <param name="ChangeName">Whether the request names the name member.</param>
/// <param name="Name">New display name; meaningful only when <paramref name="ChangeName"/> is set.</param>
/// <param name="ChangeDescription">Whether the request names the description member.</param>
/// <param name="Description">New description; meaningful only when <paramref name="ChangeDescription"/> is set.</param>
/// <param name="ExpectedVersion">Version the client read (<c>If-Match</c>), or <c>null</c> when the header was malformed.</param>
internal sealed record UpdateProductCommand(
    Guid ProductId,
    bool ChangeName,
    string? Name,
    bool ChangeDescription,
    string? Description,
    int? ExpectedVersion
);
