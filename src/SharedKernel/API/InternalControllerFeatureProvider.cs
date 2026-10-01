using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Portfolio.SharedKernel.API;

/// <summary>
/// Lets MVC discover <c>internal</c> controllers. The default provider only accepts public types, which
/// would force every use case, command, and result type a controller touches to become public as well
/// (ai/CODE.md §4.1: types are internal unless there is a reason). Controllers keep a public constructor, so
/// dependency injection activates them normally.
/// </summary>
internal sealed class InternalControllerFeatureProvider : ControllerFeatureProvider
{
    /// <inheritdoc />
    protected override bool IsController(TypeInfo typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        return typeInfo is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
            && typeof(ControllerBase).IsAssignableFrom(typeInfo)
            && !typeInfo.IsDefined(typeof(NonControllerAttribute), inherit: true);
    }
}
