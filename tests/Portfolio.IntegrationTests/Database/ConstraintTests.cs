using Npgsql;

namespace Portfolio.IntegrationTests.Database;

/// <summary>
/// The constraints the database enforces whatever the application does (ai/DATABASE.md §3.2): the safety net under
/// the domain rules. Statements are written by hand on purpose, bypassing the domain, to prove the database refuses
/// what the domain would have refused.
/// </summary>
public sealed class ConstraintTests : DatabaseTestBase
{
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";
    private const string NotNullViolation = "23502";

    private const string Timestamps = "'2026-10-01T12:00:00Z', '2026-10-01T12:00:00Z'";

    private static string Item(
        string id,
        string sku,
        int onHand,
        int reserved,
        string? deletedAt = null,
        int version = 1
    ) =>
        $"""
            INSERT INTO inventory.inventory_items (id, sku, on_hand, reserved, created_at, updated_at, deleted_at, version)
            VALUES ('{id}', '{sku}', {onHand}, {reserved}, {Timestamps}, {(
                deletedAt is null ? "NULL" : $"'{deletedAt}'"
            )}, {version})
            """;

    private static string Product(
        string id,
        string sku,
        string price = "10.0000",
        string status = "Draft",
        string name = "Cafeteira",
        string? deletedAt = null
    ) =>
        $"""
            INSERT INTO catalog.products (id, name, sku, status, price_amount, price_currency, created_at, updated_at, deleted_at, version)
            VALUES ('{id}', '{name}', '{sku}', '{status}', {price}, 'BRL', {Timestamps}, {(
                deletedAt is null ? "NULL" : $"'{deletedAt}'"
            )}, 1)
            """;

    private static string User(string id, string email, string level = "Public", string? deletedAt = null) =>
        $"""
            INSERT INTO identity.users (id, email, password_hash, access_level, status, token_version, failed_sign_ins, created_at, updated_at, deleted_at, version)
            VALUES ('{id}', '{email}', 'hash', '{level}', 'Active', 1, 0, {Timestamps}, {(
                deletedAt is null ? "NULL" : $"'{deletedAt}'"
            )}, 1)
            """;

    private static string Movement(string id, string itemId, int delta, string? key = null) =>
        $"""
            INSERT INTO inventory.stock_movements (id, inventory_item_id, delta, reason_code, on_hand_after, recorded_by, idempotency_key, created_at, updated_at)
            VALUES ('{id}', '{itemId}', {delta}, 'stocktake', 5, '{Guid.NewGuid()}', {(
                key is null ? "NULL" : $"'{key}'"
            )}, {Timestamps})
            """;

    private static string NewId() => Guid.CreateVersion7().ToString();

    private async Task ShouldRejectAsync(string sql, string sqlState)
    {
        var failure = await Should.ThrowAsync<PostgresException>(() => Database.ExecuteAsync(sql));

        failure.SqlState.ShouldBe(sqlState);
    }

    [Fact]
    public async Task Should_accept_a_valid_row_so_the_rejections_below_are_about_the_constraint()
    {
        await Database.ExecuteAsync(Item(NewId(), "CAF-600-PRT", onHand: 10, reserved: 4));

        (await Database.ScalarAsync("SELECT count(*) FROM inventory.inventory_items")).ShouldBe(1L);
    }

    [Theory]
    [InlineData(3, 4)]
    [InlineData(5, -1)]
    [InlineData(10_000_001, 0)]
    public async Task Should_reject_stock_that_breaks_the_bounds_of_br_inv_001(int onHand, int reserved)
    {
        await ShouldRejectAsync(Item(NewId(), "CAF-600-PRT", onHand, reserved), CheckViolation);
    }

    [Fact]
    public async Task Should_reject_a_version_below_one_on_any_aggregate_table()
    {
        await ShouldRejectAsync(Item(NewId(), "CAF-600-PRT", 1, 0, version: 0), CheckViolation);
    }

    [Fact]
    public async Task Should_allow_the_sku_of_a_deleted_item_to_be_reused_but_not_an_active_one()
    {
        await Database.ExecuteAsync(Item(NewId(), "CAF-600-PRT", 1, 0, deletedAt: "2026-10-01T13:00:00Z"));
        await Database.ExecuteAsync(Item(NewId(), "CAF-600-PRT", 1, 0));

        await ShouldRejectAsync(Item(NewId(), "CAF-600-PRT", 1, 0), UniqueViolation);
    }

    [Theory]
    [InlineData("0.0000", "Draft", "Cafeteira")]
    [InlineData("-1.0000", "Draft", "Cafeteira")]
    [InlineData("10.0000", "Bogus", "Cafeteira")]
    [InlineData("10.0000", "Draft", "Ab")]
    public async Task Should_reject_a_product_that_breaks_a_catalog_rule(string price, string status, string name)
    {
        await ShouldRejectAsync(Product(NewId(), "CAF-600-PRT", price, status, name), CheckViolation);
    }

    [Fact]
    public async Task Should_reject_two_active_products_with_the_same_sku_but_allow_reuse_after_deletion()
    {
        await Database.ExecuteAsync(Product(NewId(), "CAF-600-PRT", deletedAt: "2026-10-01T13:00:00Z"));
        await Database.ExecuteAsync(Product(NewId(), "CAF-600-PRT"));

        await ShouldRejectAsync(Product(NewId(), "CAF-600-PRT"), UniqueViolation);
    }

    [Fact]
    public async Task Should_reject_an_unknown_access_level_and_a_duplicate_active_email()
    {
        await ShouldRejectAsync(User(NewId(), "a@example.com", level: "Root"), CheckViolation);

        await Database.ExecuteAsync(User(NewId(), "a@example.com", deletedAt: "2026-10-01T13:00:00Z"));
        await Database.ExecuteAsync(User(NewId(), "a@example.com"));
        await ShouldRejectAsync(User(NewId(), "a@example.com"), UniqueViolation);
    }

    [Fact]
    public async Task Should_reject_a_missing_required_value()
    {
        await ShouldRejectAsync(
            $"INSERT INTO identity.users (id, access_level, status, token_version, failed_sign_ins, created_at, updated_at, version) VALUES ('{NewId()}', 'Public', 'Active', 1, 0, {Timestamps}, 1)",
            NotNullViolation
        );
    }

    [Fact]
    public async Task Should_reject_a_ledger_line_that_moves_nothing_or_belongs_to_no_item()
    {
        var item = NewId();
        await Database.ExecuteAsync(Item(item, "CAF-600-PRT", 5, 0));

        await ShouldRejectAsync(Movement(NewId(), item, delta: 0), CheckViolation);
        await ShouldRejectAsync(Movement(NewId(), NewId(), delta: 1), ForeignKeyViolation);
    }

    [Fact]
    public async Task Should_let_a_key_record_one_movement_per_item_but_allow_many_without_a_key()
    {
        var first = NewId();
        var second = NewId();
        await Database.ExecuteAsync(Item(first, "CAF-600-PRT", 5, 0));
        await Database.ExecuteAsync(Item(second, "FIL-100-PAP", 5, 0));

        await Database.ExecuteAsync(Movement(NewId(), first, 1, "key-0001"));
        await Database.ExecuteAsync(Movement(NewId(), second, 1, "key-0001"));
        await Database.ExecuteAsync(Movement(NewId(), first, 1));
        await Database.ExecuteAsync(Movement(NewId(), first, 1));

        await ShouldRejectAsync(Movement(NewId(), first, 1, "key-0001"), UniqueViolation);
    }

    [Fact]
    public async Task Should_reject_a_refresh_token_that_outlives_its_family_or_has_no_account()
    {
        var user = NewId();
        await Database.ExecuteAsync(User(user, "a@example.com"));

        string Token(string account, string expires, string familyExpires, string hash) =>
            $"""
                INSERT INTO identity.refresh_tokens (id, user_id, family_id, token_hash, expires_at, family_expires_at, created_at, updated_at, version)
                VALUES ('{NewId()}', '{account}', '{NewId()}', '{hash}', '{expires}', '{familyExpires}', {Timestamps}, 1)
                """;

        await ShouldRejectAsync(Token(user, "2026-11-01T00:00:00Z", "2026-10-15T00:00:00Z", "h1"), CheckViolation);
        await ShouldRejectAsync(
            Token(NewId(), "2026-10-02T00:00:00Z", "2026-10-15T00:00:00Z", "h2"),
            ForeignKeyViolation
        );

        await Database.ExecuteAsync(Token(user, "2026-10-02T00:00:00Z", "2026-10-15T00:00:00Z", "h3"));
        await ShouldRejectAsync(Token(user, "2026-10-02T00:00:00Z", "2026-10-15T00:00:00Z", "h3"), UniqueViolation);
    }

    [Fact]
    public async Task Should_reject_a_negative_outbox_attempt_count_and_a_duplicate_event()
    {
        var id = NewId();
        string Outbox(string eventId, int attempts) =>
            $"""
                INSERT INTO inventory.outbox_messages (id, type, payload, aggregate_id, aggregate_version, occurred_at, attempts)
                VALUES ('{eventId}', 'T', '[]', '{NewId()}', 1, '2026-10-01T12:00:00Z', {attempts})
                """;

        await ShouldRejectAsync(Outbox(NewId(), -1), CheckViolation);

        await Database.ExecuteAsync(Outbox(id, 0));
        await ShouldRejectAsync(Outbox(id, 0), UniqueViolation);
    }
}
