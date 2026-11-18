namespace Sinedu.SharedKernel.Security;

/// <summary>
/// Marca tipos ou propriedades com dados sensíveis (Saúde, Proteção/Sigilo, segredos).
/// Tipos marcados nunca são serializados em logs, auditoria ou telemetria.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Field, Inherited = true)]
public sealed class SensitiveAttribute : Attribute;
