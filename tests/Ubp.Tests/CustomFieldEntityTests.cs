using Ubp.Core.Models;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

public sealed class CustomFieldEntityTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public CustomFieldEntityTests(TestDb db) => _db = db;

    [Fact]
    public async Task Item_custom_field_is_required_validated_and_persisted()
    {
        var actor = await _db.AdminActorAsync();
        await _db.CustomFields().CreateAsync(actor, new CustomFieldInput
        {
            EntityName = "items",
            FieldKey = "shelf",
            LabelAr = "الرف",
            DataType = "text",
            IsRequired = true
        });

        var unit = await _db.UnitRepo.CreateAsync(new UnitInput { Code = "CF-U", NameAr = "وحدة" });
        var input = new ItemInput
        {
            Code = "IT-CF",
            NameAr = "صنف بحقل مخصص",
            BaseUnitId = unit.Id,
            Units = new List<ItemUnitInput> { new() { UnitId = unit.Id, ConversionFactor = 1 } }
        };

        var missing = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.ItemService().CreateAsync(actor, input));
        Assert.Equal("validation.custom_field_required", missing.Code);

        input.CustomFields = new Dictionary<string, object?> { ["shelf"] = "A-12" };
        var created = await _db.ItemService().CreateAsync(actor, input);
        var detail = await _db.ItemService().GetDetailAsync(actor, created.Id);

        Assert.True(detail.Item.CustomFields.ContainsKey("shelf"));
        Assert.Equal("A-12", detail.Item.CustomFields["shelf"]?.ToString());
    }

    [Fact]
    public async Task Unsupported_entity_is_rejected()
    {
        var actor = await _db.AdminActorAsync();
        var exception = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.CustomFields().CreateAsync(actor, new CustomFieldInput
            {
                EntityName = "invoices", FieldKey = "x", LabelAr = "حقل", DataType = "text"
            }));
        Assert.Equal("validation.entity_not_supported", exception.Code);
    }
}
