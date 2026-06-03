namespace Imbalances.Core.Models;

public class ReciprocidadResultado
{
    public string Id { get; set; } = string.Empty;
    public string EmpresaCxc { get; set; } = string.Empty;
    public string EmpresaCxp { get; set; } = string.Empty;
    public string GrupoReciprocidad { get; set; } = string.Empty;
    public decimal ValorCxc { get; set; }
    public decimal ValorCxp { get; set; }
    public decimal Diferencia { get; set; }
    public bool DentroTolerancia { get; set; }
    public bool FaltaContraparteCxc { get; set; }
    public bool FaltaContraparteCxp { get; set; }
}
