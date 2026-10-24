using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Features.Sales.CreateSale;

namespace Makari.Api.Features.Sales;

public static class SaleOrderValidator
{
    public static Dictionary<string, string[]> Validate(CustomerType customerType, IReadOnlyList<CreateSaleItemRequest>? items)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string key, string message)
        {
            if (!errors.TryGetValue(key, out var messages)) errors[key] = messages = [];
            messages.Add(message);
        }
        if (!Enum.IsDefined(customerType)) Add("customerType", "Specify Local or Visitor.");
        if (items is null || items.Count == 0)
        {
            Add("items", "At least one item is required.");
        }
        else
        {
            foreach (var group in items.Select((item, index) => (item, index))
                         .Where(pair => pair.item is not null)
                         .GroupBy(pair => pair.item.ProductVariantId).Where(group => group.Count() > 1))
            {
                foreach (var duplicate in group)
                    Add($"items[{duplicate.index}].productVariantId", "A variant can appear only once per sale.");
            }

            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                if (item is null)
                {
                    Add($"items[{index}]", "An item is required.");
                    continue;
                }
                if (item.ProductVariantId == Guid.Empty)
                    Add($"items[{index}].productVariantId", "A product variant ID is required.");
                if (item.Quantity <= 0)
                    Add($"items[{index}].quantity", "Quantity must be greater than zero.");
            }
        }

        return errors.ToDictionary(item => item.Key, item => item.Value.ToArray());
    }
}
