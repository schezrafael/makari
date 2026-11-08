namespace Makari.Api.Features.Sales.Domain;

public enum SaleCondition { Normal = 0, LocalDiscount = 1, Courtesy = 2, Staff = 3, Birthday = 4 }

public static class SaleConditions
{
    public static bool IsFree(this SaleCondition condition) => condition is SaleCondition.Courtesy or SaleCondition.Staff or SaleCondition.Birthday;
}
