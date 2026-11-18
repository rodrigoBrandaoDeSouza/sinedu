using Sinedu.Tenant.School;

namespace Sinedu.UnitTests.Imports;

public class ClassroomLabelTests
{
    [Theory]
    // Formato do cadastro na planilha real
    [InlineData("6º A Manhã", "6º", Stage.Fundamental, "A", Shift.Manha, null)]
    [InlineData("1ª D Noite", "1ª", Stage.Medio, "D", Shift.Noite, null)]
    [InlineData("9º E Tarde", "9º", Stage.Fundamental, "E", Shift.Tarde, null)]
    // Formato das abas de frequência (com código da turma)
    [InlineData("9º Série A Manhã - 9000001", "9º", Stage.Fundamental, "A", Shift.Manha, "9000001")]
    [InlineData("1º Série B Manhã - 1234567", "1ª", Stage.Medio, "B", Shift.Manha, "1234567")]
    [InlineData("3º Série C Noite - 7654321", "3ª", Stage.Medio, "C", Shift.Noite, "7654321")]
    // Variações comuns
    [InlineData("6º ano A - Manhã", "6º", Stage.Fundamental, "A", Shift.Manha, null)]
    [InlineData("7o B tarde", "7º", Stage.Fundamental, "B", Shift.Tarde, null)]
    [InlineData("2ª série C noturno", "2ª", Stage.Medio, "C", Shift.Noite, null)]
    [InlineData("8º C Vespertino", "8º", Stage.Fundamental, "C", Shift.Tarde, null)]
    public void Reconhece_formatos_das_escolas(string label, string grade, Stage stage, string section, Shift shift, string? code)
    {
        Assert.True(ClassroomLabel.TryParse(label, out var key, out var externalCode));
        Assert.Equal(new ClassroomKey(grade, stage, section, shift), key);
        Assert.Equal(code, externalCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("TURMA-INEXISTENTE-XYZ")]
    [InlineData("6º Manhã")]
    [InlineData("A Manhã")]
    [InlineData("6º A")]
    public void Recusa_rotulos_incompletos(string label) =>
        Assert.False(ClassroomLabel.TryParse(label, out _, out _));
}
