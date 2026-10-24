namespace Makari.Api.Features.Sales.Domain;

internal static class SaleAmounts
{
    public static decimal Subtotal(decimal unitPrice, int quantity) => checked(unitPrice * quantity);
}
