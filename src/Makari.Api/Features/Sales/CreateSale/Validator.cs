using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Features.Sales.Domain;

namespace Makari.Api.Features.Sales.CreateSale;

public static class CreateSaleValidator
{
    public static Dictionary<string, string[]> Validate(CreateSaleRequest? request)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string key, string message)
        {
            if (!errors.TryGetValue(key, out var messages)) errors[key] = messages = [];
            messages.Add(message);
        }

        if (request is null)
        {
            Add("request", "A request body is required.");
            return errors.ToDictionary(item => item.Key, item => item.Value.ToArray());
        }

        if (request.IdempotencyKey == Guid.Empty) Add("idempotencyKey", "A non-empty idempotency key is required.");
        if (!Enum.IsDefined(request.CustomerType)) Add("customerType", "Specify Local or Visitor.");
        if (!Enum.IsDefined(request.PaymentMethod)) Add("paymentMethod", "Specify Cash or Transfer.");
        if (request.Items is null || request.Items.Count == 0)
        {
            Add("items", "At least one item is required.");
        }
        else
        {
            foreach (var group in request.Items.Select((item, index) => (item, index))
                         .Where(pair => pair.item is not null)
                         .GroupBy(pair => pair.item.ProductVariantId).Where(group => group.Count() > 1))
            {
                foreach (var duplicate in group)
                    Add($"items[{duplicate.index}].productVariantId", "A variant can appear only once per sale.");
            }

            for (var index = 0; index < request.Items.Count; index++)
            {
                var item = request.Items[index];
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
