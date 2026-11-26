// --------------------------------------------------------------------------------------------------------------------
// <copyright file="MethodExecutor.cs" company="sgmunn">
//   (c) sgmunn 2012  
//
//   Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated 
//   documentation files (the "Software"), to deal in the Software without restriction, including without limitation 
//   the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and 
//   to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//
//   The above copyright notice and this permission notice shall be included in all copies or substantial portions of 
//   the Software.
// 
//   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO 
//   THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
//   AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF 
//   CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS 
//   IN THE SOFTWARE.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace Mobile.CQRS;

/// <summary>Dispatches one exact-parameter void instance handler, preferring the nearest declaring type.</summary>
public static class MethodExecutor
{
    private static readonly ConcurrentDictionary<(Type Target, Type Parameter), MethodInfo> Methods = new();

    public static bool ExecuteMethod(object instance, object param)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(param);
        var method = Methods.GetOrAdd((instance.GetType(), param.GetType()), key => Find(key.Target, key.Parameter));
        if (method == null) return false;
        method.Invoke(instance, new[] { param });
        return true;
    }

    private static MethodInfo Find(Type target, Type parameter)
    {
        for (var type = target; type != null; type = type.BaseType)
        {
            var candidates = type.GetTypeInfo().DeclaredMethods.Where(method =>
                !method.IsStatic && !method.ContainsGenericParameters && method.ReturnType == typeof(void)
                && method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType == parameter).ToArray();
            if (candidates.Length > 1) throw new AmbiguousMatchException($"Multiple handlers for {parameter} on {type}.");
            if (candidates.Length == 1) return candidates[0];
        }
        return null;
    }
}
