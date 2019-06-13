using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace EmitSample
{
    class Program
    {
        static void Main(string[] args)
        {
            var obj = (IValuable)new MyClass()
                .Implements(typeof(IValuable))
                .Implements(typeof(ILogger));

            obj.Value = "Hello, world!";
            ((ILogger)obj).Log(obj.Value);
        }
    }

    public interface IValuable
    {
        string Value { get; set; }
    }

    public interface ILogger
    {
        void Log(string text);
    }

    public class MyClass
    {
        public virtual void Log(string text)
        {
            Console.WriteLine(text);
        }
    }

    static class TypeMixer
    {
        public static object Implements(this object src, Type interfaceType)
        {
            var srcType = src.GetType();

            var assemblyName    = new AssemblyName() { Name = Guid.NewGuid().ToString() };
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
            var moduleBuilder   = assemblyBuilder.DefineDynamicModule("Module");
            var typeBuilder     = moduleBuilder  .DefineType(srcType.Name + "_" + interfaceType.Name, TypeAttributes.Public, srcType);
            typeBuilder.AddInterfaceImplementation(interfaceType);

            var memberList      = new List<string>();
            var visibilityFlags = BindingFlags.Instance;

            foreach (var p in interfaceType.GetProperties())
            {
                memberList.Add(p.Name);

                if (p.CanRead && p.CanWrite) DefineGetterAndSetter(p, typeBuilder);
                else if (p.CanRead ) DefineGetter(p, typeBuilder);
                else if (p.CanWrite) DefineSetter(p, typeBuilder);
            }

            if (src != null)
            {
                foreach (var m in interfaceType.GetMethods(visibilityFlags))
                {
                    if (m.Name.StartsWith("get_") || m.Name.StartsWith("set_")) continue;

                    var mSrc = srcType.GetMethod(m.Name);
                    if (mSrc == null) continue;
                    memberList.Add(m.Name);

                    DefineMethodOverrideWithArgs_1(m, typeBuilder, mSrc);
                }

                foreach (var m in srcType.GetMethods(visibilityFlags))
                {
                    if (memberList.Contains(m.Name)) continue;
                    memberList.Add(m.Name);

                    DefineMethodOverrideWithArgs_1(m, typeBuilder, m);
                }

                foreach (var p in srcType.GetProperties(visibilityFlags))
                {
                    if (memberList.Contains(p.Name)) continue;
                    memberList.Add(p.Name);

                    if (p.CanRead && p.CanWrite) DefineGetterAndSetter(p, typeBuilder);
                    else if (p.CanRead ) DefineGetter(p, typeBuilder);
                    else if (p.CanWrite) DefineSetter(p, typeBuilder);
                }
            }

            var newObj = Activator.CreateInstance(typeBuilder.CreateType());

            return src == null ? newObj : CopyValues(src, newObj);
        }

        private static void DefineGetter(PropertyInfo p, TypeBuilder typeBuilder)
        {
            var propertyFlags = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.Virtual;

            var fieldBuilder    = typeBuilder.DefineField("_" + p.Name.ToLower(), p.PropertyType, FieldAttributes.Private);
            var propertyBuilder = typeBuilder.DefineProperty(p.Name, PropertyAttributes.None, p.PropertyType, new Type[0]);
            var getterBuilder   = typeBuilder.DefineMethod("get_" + p.Name, propertyFlags, p.PropertyType, new Type[0]);
            var getGenerator    = getterBuilder.GetILGenerator();

            getGenerator.Emit(OpCodes.Ldarg_0);
            getGenerator.Emit(OpCodes.Ldfld, fieldBuilder);
            getGenerator.Emit(OpCodes.Ret);

            propertyBuilder.SetGetMethod(getterBuilder);
            typeBuilder.DefineMethodOverride(getterBuilder, p.GetGetMethod());
        }

        private static void DefineSetter(PropertyInfo p, TypeBuilder typeBuilder)
        {
            var propertyFlags = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.Virtual;

            var fieldBuilder    = typeBuilder.DefineField("_" + p.Name.ToLower(), p.PropertyType, FieldAttributes.Private);
            var propertyBuilder = typeBuilder.DefineProperty(p.Name, PropertyAttributes.None, p.PropertyType, new Type[0]);
            var setterBuilder   = typeBuilder.DefineMethod("set_" + p.Name, propertyFlags, null, new[] { p.PropertyType });
            var setGenerator    = setterBuilder.GetILGenerator();

            setGenerator.Emit(OpCodes.Ldarg_0);
            setGenerator.Emit(OpCodes.Ldarg_1);
            setGenerator.Emit(OpCodes.Stfld, fieldBuilder);
            setGenerator.Emit(OpCodes.Ret);

            propertyBuilder.SetSetMethod(setterBuilder);
            typeBuilder.DefineMethodOverride(setterBuilder, p.GetSetMethod());
        }

        private static void DefineGetterAndSetter(PropertyInfo p, TypeBuilder typeBuilder)
        {
            var propertyFlags = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.Virtual;

            var fieldBuilder    = typeBuilder.DefineField("_" + p.Name.ToLower(), p.PropertyType, FieldAttributes.Private);
            var propertyBuilder = typeBuilder.DefineProperty(p.Name, PropertyAttributes.None, p.PropertyType, new Type[0]);
            var getterBuilder   = typeBuilder.DefineMethod("get_" + p.Name, propertyFlags, p.PropertyType, new Type[0]);
            var setterBuilder   = typeBuilder.DefineMethod("set_" + p.Name, propertyFlags, null, new[] { p.PropertyType });
            var getGenerator    = getterBuilder.GetILGenerator();
            var setGenerator    = setterBuilder.GetILGenerator();

            getGenerator.Emit(OpCodes.Ldarg_0);
            getGenerator.Emit(OpCodes.Ldfld, fieldBuilder);
            getGenerator.Emit(OpCodes.Ret);

            setGenerator.Emit(OpCodes.Ldarg_0);
            setGenerator.Emit(OpCodes.Ldarg_1);
            setGenerator.Emit(OpCodes.Stfld, fieldBuilder);
            setGenerator.Emit(OpCodes.Ret);

            propertyBuilder.SetGetMethod(getterBuilder);
            propertyBuilder.SetSetMethod(setterBuilder);
            typeBuilder.DefineMethodOverride(getterBuilder, p.GetGetMethod());
            typeBuilder.DefineMethodOverride(setterBuilder, p.GetSetMethod());
        }

        private static void DefineMethodOverrideWithArgs_1(MethodInfo m, TypeBuilder typeBuilder, MethodInfo mBody)
        {
            var methodFlags = MethodAttributes.Public | MethodAttributes.Virtual;

            var paramTypes      = m.GetParameters().Select(p => p.ParameterType).ToArray();
            var methodBuilder   = typeBuilder.DefineMethod("_" + m.Name.ToLower(), methodFlags, m.ReturnType, paramTypes);
            var methodGenerator = methodBuilder.GetILGenerator();

            methodGenerator.Emit(OpCodes.Ldarg_0);
            methodGenerator.Emit(OpCodes.Ldarg_1);
            methodGenerator.Emit(OpCodes.Call, mBody);
            methodGenerator.Emit(OpCodes.Ret);

            typeBuilder.DefineMethodOverride(methodBuilder, m);
        }

        private static object CopyValues(object src, object dst)
        {
            var visibilityFlags = BindingFlags.Public | BindingFlags.Instance;

            foreach (var srcProp in src.GetType().GetProperties(visibilityFlags))
            {
                var dstProp = dst.GetType().GetProperty(srcProp.Name, visibilityFlags);
                if (dstProp != null && dstProp.CanWrite)
                    dstProp.SetValue(dst, srcProp.GetValue(src), null);
            }

            return dst;
        }
    }
}
