namespace simple_bloomberg_terminal.Models.Enums;

public enum ReviewableField
{
    VALUE,
    PERCENTAGE,
    NAME,
    RELATED_COMPANY,
    CLASSIFICATION,
    // Risk-node free-text note backing a CompanyRisk row (CLASSIFICATION doubles as its Scope cell).
    NOTE
}
