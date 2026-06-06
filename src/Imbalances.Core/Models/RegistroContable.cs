namespace Imbalances.Core.Models;

public class RegistroContable
{
    public string Empresa { get; set; } = string.Empty;
    public string EmpresaContraparte { get; set; } = string.Empty;
    public string Cuenta { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string Nota { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public string ArchivoOrigen { get; set; } = string.Empty;
    public string HojaOrigen { get; set; } = string.Empty;
    public string TextoOrigen { get; set; } = string.Empty;
    public string CompanyCode { get; set; } = string.Empty;
    public string TradePartnerCode { get; set; } = string.Empty;
    public string ConcOp { get; set; } = string.Empty;
}