﻿/*
    Copyright (C) 2014 Omega software d.o.o.

    This file is part of Rhetos.

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as
    published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using Rhetos.Processing.DefaultCommands;
using Rhetos.Utilities;
using System.Collections;

namespace Rhetos.Dom.DefaultConcepts
{
    public class GenericFilterHelper
    {
        IDomainObjectModel _domainObjectModel;

        public GenericFilterHelper(IDomainObjectModel domainObjectModel)
        {
            _domainObjectModel = domainObjectModel;
        }

        //================================================================
        #region Property filters

        public Expression<Func<T, bool>> ToExpression<T>(IEnumerable<PropertyFilter> propertyFilters)
        {
            return (Expression<Func<T, bool>>)ToExpression(propertyFilters, typeof(T));
        }

        public LambdaExpression ToExpression(IEnumerable<PropertyFilter> propertyFilters, Type parameterType)
        {
            ParameterExpression parameter = Expression.Parameter(parameterType, "p");

            if (propertyFilters == null || propertyFilters.Count() == 0)
                return Expression.Lambda(Expression.Constant(true), parameter);

            Expression resultCondition = null;

            foreach (var criteria in propertyFilters)
            {
                if (string.IsNullOrEmpty(criteria.Property))
                    continue;

                Expression memberAccess = null;
                foreach (var property in criteria.Property.Split('.'))
                {
                    var parentExpression = memberAccess ?? (Expression)parameter;
                    if (parentExpression.Type.GetProperty(property) == null)
                        throw new ClientException("Invalid generic filter parameter: Type '" + parentExpression.Type.FullName + "' does not have property '" + property + "'.");
                    memberAccess = Expression.Property(parentExpression, property);
                }

                // Change the type of the parameter 'value'. it is necessary for comparisons (specially for booleans)

                bool memberIsNullableValueType = memberAccess.Type.IsGenericType && memberAccess.Type.GetGenericTypeDefinition() == typeof(Nullable<>);
                Type basicType = memberIsNullableValueType ? memberAccess.Type.GetGenericArguments().Single() : memberAccess.Type;

                ConstantExpression constant;
                // Operations 'equal' and 'notequal' are supported for backward compatibility.
                if (new[] { "equals", "equal", "notequals", "notequal", "greater", "greaterequal", "less", "lessequal" }.Contains(criteria.Operation, StringComparer.OrdinalIgnoreCase))
                {
                    // Constant value should be of same type as the member it is compared to.

                    object convertedValue;
                    if (criteria.Value == null || basicType.IsAssignableFrom(criteria.Value.GetType()))
                        convertedValue = criteria.Value;
                    else if (basicType == typeof(Guid) && criteria.Value is string) // Guid object's type was not automatically recognized when deserializing from JSON.
                        convertedValue = Guid.Parse(criteria.Value.ToString());
                    else if (basicType == typeof(DateTime) && criteria.Value is string) // DateTime object's type was not automatically recognized when deserializing from JSON.
                    {
                        string dateString = "\"" + ((string)criteria.Value).Replace("/", "\\/") + "\"";
                        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(dateString)))
                        {
                            var serializer = new DataContractJsonSerializer(typeof(DateTime));
                            try
                            {
                                convertedValue = (DateTime)serializer.ReadObject(stream);
                            }
                            catch (SerializationException ex)
                            {
                                throw new ClientException("Invalid JSON format of " + basicType.Name + " property '" + criteria.Property + "'. " + ex.Message, ex);
                            }
                        }
                    }
                    else if ((basicType == typeof(decimal) || basicType == typeof(int)) && criteria.Value is string)
                        throw new FrameworkException("Invalid JSON format of " + basicType.Name + " property '" + criteria.Property + "'. Numeric value should not be passed as a string in JSON serialized object.");
                    else
                        convertedValue = Convert.ChangeType(criteria.Value, basicType);

                    if (convertedValue == null && memberAccess.Type.IsValueType && !memberIsNullableValueType)
                    {
                        Type nullableMemberType = typeof(Nullable<>).MakeGenericType(memberAccess.Type);
                        memberAccess = Expression.Convert(memberAccess, nullableMemberType);
                    }

                    constant = Expression.Constant(convertedValue, memberAccess.Type);
                }
                else if (new[] { "startswith", "endswith", "contains", "notcontains" }.Contains(criteria.Operation, StringComparer.OrdinalIgnoreCase))
                {
                    // Constant value should be string.
                    constant = Expression.Constant(criteria.Value.ToString(), typeof(string));
                }
                else if (new[] { "datein", "datenotin" }.Contains(criteria.Operation, StringComparer.OrdinalIgnoreCase))
                {
                    constant = null;
                }
                else
                    throw new FrameworkException("Unsupported generic filter operation '" + criteria.Operation + "' on property.");

                Expression expression;
                switch (criteria.Operation.ToLower())
                {
                    case "equals":
                    case "equal":
                        if (basicType == typeof(string))
                            expression = Expression.Call(typeof(DatabaseExtensionFunctions).GetMethod("EqualsCaseInsensitive"), memberAccess, constant);
                        else
                            expression = Expression.Equal(memberAccess, constant);
                        break;
                    case "notequals":
                    case "notequal":
                        if (basicType == typeof(string))
                            expression = Expression.Call(typeof(DatabaseExtensionFunctions).GetMethod("NotEqualsCaseInsensitive"), memberAccess, constant);
                        else
                            expression = Expression.NotEqual(memberAccess, constant);
                        break;
                    case "greater":
                        if (basicType == typeof(string))
                            expression = Expression.Call(typeof(DatabaseExtensionFunctions).GetMethod("IsGreaterThen"), memberAccess, constant);
                        else
                            expression = Expression.GreaterThan(memberAccess, constant);
                        break;
                    case "greaterequal":
                        if (basicType == typeof(string))
                            expression = Expression.Call(typeof(DatabaseExtensionFunctions).GetMethod("IsGreaterThenOrEqual"), memberAccess, constant);
                        else
                            expression = Expression.GreaterThanOrEqual(memberAccess, constant);
                        break;
                    case "less":
                        if (basicType == typeof(string))
                            expression = Expression.Call(typeof(DatabaseExtensionFunctions).GetMethod("IsLessThen"), memberAccess, constant);
                        else
                            expression = Expression.LessThan(memberAccess, constant);
                        break;
                    case "lessequal":
                        if (basicType == typeof(string))
                            expression = Expression.Call(typeof(DatabaseExtensionFunctions).GetMethod("IsLessThenOrEqual"), memberAccess, constant);
                        else
                            expression = Expression.LessThanOrEqual(memberAccess, constant);
                        break;
                    case "startswith":
                    case "endswith":
                        {
                            Expression stringMember;
                            if (basicType == typeof(string))
                                stringMember = memberAccess;
                            else
                            {
                                var castMethod = typeof(DatabaseExtensionFunctions).GetMethod("CastToString", new[] { memberAccess.Type });
                                if (castMethod == null)
                                    throw new FrameworkException("Generic filter operation '" + criteria.Operation + "' is not supported on property type '" + basicType.Name + "'. There is no overload of 'DatabaseExtensionFunctions.CastToString' function for the type.");
                                stringMember = Expression.Call(castMethod, memberAccess);
                            }
                            string dbMethodName = criteria.Operation.Equals("startswith", StringComparison.OrdinalIgnoreCase) ? "StartsWithCaseInsensitive" : "EndsWithCaseInsensitive";
                            expression = Expression.Call(typeof(DatabaseExtensionFunctions).GetMethod(dbMethodName), stringMember, constant);
                            break;
                        }
                    case "contains":
                    case "notcontains":
                        {
                            Expression stringMember;
                            if (basicType == typeof(string))
                                stringMember = memberAccess;
                            else
                            {
                                var castMethod = typeof(DatabaseExtensionFunctions).GetMethod("CastToString", new[] { memberAccess.Type });
                                if (castMethod == null)
                                    throw new FrameworkException("Generic filter operation '" + criteria.Operation + "' is not supported on property type '" + basicType.Name + "'. There is no overload of 'DatabaseExtensionFunctions.CastToString' function for the type.");
                                stringMember = Expression.Call(castMethod, memberAccess);
                            }
                            expression = Expression.Call(typeof(DatabaseExtensionFunctions).GetMethod("ContainsCaseInsensitive"), stringMember, constant);

                            if (criteria.Operation.Equals("notcontains", StringComparison.OrdinalIgnoreCase))
                                expression = Expression.Not(expression);
                            break;
                        }
                    case "datein":
                    case "datenotin":
                        {
                            if (basicType != typeof(DateTime))
                                throw new FrameworkException("Generic filter operation '" + criteria.Operation
                                    + "' is not supported for property type '" + basicType.Name + "'. Expected property type 'DateTime'.");

                            var match = DateRangeRegex.Match(criteria.Value.ToString());
                            if (!match.Success)
                                throw new FrameworkException("Generic filter operation '" + criteria.Operation + "' expects format 'yyyy-mm-dd', 'yyyy-mm' or 'yyyy'. Value '" + criteria.Value + "' has invalid format.");

                            DateTime? date1, date2;
                            int year = int.Parse(match.Groups["y"].Value);
                            if (string.IsNullOrEmpty(match.Groups["m"].Value))
                            {
                                date1 = new DateTime(year, 1, 1);
                                date2 = date1.Value.AddYears(1);
                            }
                            else
                            {
                                int month = int.Parse(match.Groups["m"].Value);
                                if (string.IsNullOrEmpty(match.Groups["d"].Value))
                                {
                                    date1 = new DateTime(year, month, 1);
                                    date2 = date1.Value.AddMonths(1);
                                }
                                else
                                {
                                    int day = int.Parse(match.Groups["d"].Value);
                                    date1 = new DateTime(year, month, day);
                                    date2 = date1.Value.AddDays(1);
                                }
                            }

                            expression = Expression.AndAlso(
                                Expression.GreaterThanOrEqual(memberAccess, Expression.Constant(date1, typeof(DateTime?))),
                                Expression.LessThan(memberAccess, Expression.Constant(date2, typeof(DateTime?))));

                            if (criteria.Operation.Equals("datenotin", StringComparison.OrdinalIgnoreCase))
                                expression = Expression.Not(expression);
                            break;
                        }
                    default:
                        throw new FrameworkException("Unsupported generic filter operation '" + criteria.Operation + "' on property (while generating expression).");
                }

                resultCondition = resultCondition != null ? Expression.AndAlso(resultCondition, expression) : expression;
            }

            return Expression.Lambda(resultCondition, parameter);
        }

        private static readonly Regex DateRangeRegex = new Regex(@"^(?<y>\d{4})(-(?<m>\d{1,2}))?(-(?<d>\d{1,2}))?$");

        #endregion
        //================================================================
        #region Sorting and paging

        public static IQueryable<T> SortAndPaginate<T>(IQueryable<T> query, ReadCommandInfo commandInfo)
        {
            bool pagingIsUsed = commandInfo.Top > 0 || commandInfo.Skip > 0;

            if (pagingIsUsed && (commandInfo.OrderByProperties == null || commandInfo.OrderByProperties.Length == 0))
                throw new ClientException("Invalid ReadCommand argument: Sort order must be set if paging is used (Top or Skip).");

            if (commandInfo.OrderByProperties != null)
                foreach (var order in commandInfo.OrderByProperties)
                    query = Sort(query, order.Property,
                        ascending: !order.Descending,
                        firstProperty: order == commandInfo.OrderByProperties.First());

            Type itemType = query.GetType().GetInterface("IQueryable`1").GetGenericArguments().Single();

            if (commandInfo.Skip > 0)
            {
                // "query.Skip(commandInfo.Skip)" would convert the result IQueryable<T> and not use the actual queryable generic type.
                var skipMethod = typeof(Queryable).GetMethod("Skip").MakeGenericMethod(itemType);
                query = (IQueryable<T>)skipMethod.InvokeEx(null, query, commandInfo.Skip);
            }

            if (commandInfo.Top > 0)
            {
                // "query.Take(commandInfo.Top)" would convert the result IQueryable<T> and not use the actual queryable generic type.
                var takeMethod = typeof(Queryable).GetMethod("Take").MakeGenericMethod(itemType);
                query = (IQueryable<T>)takeMethod.InvokeEx(null, query, commandInfo.Top);
            }

            return query;
        }

        public static IQueryable<T> Sort<T>(IQueryable<T> source, string orderByProperty, bool ascending = true, bool firstProperty = false)
        {
            if (string.IsNullOrEmpty(orderByProperty))
                return source;

            Type itemType = source.GetType().GetInterface("IQueryable`1").GetGenericArguments().Single();

            ParameterExpression parameter = Expression.Parameter(itemType, "posting");
            Expression property = Expression.Property(parameter, orderByProperty);
            LambdaExpression propertySelector = Expression.Lambda(property, new[] { parameter });

            MethodInfo orderMethod = firstProperty
                    ? (ascending ? OrderByAscendingMethod : OrderByDescendingMethod)
                    : (ascending ? ThenByAscendingMethod : ThenByDescendingMethod);
            MethodInfo genericOrderMethod = orderMethod.MakeGenericMethod(new[] { itemType, property.Type });

            return (IQueryable<T>)genericOrderMethod.InvokeEx(null, source, propertySelector);
        }

        private static readonly MethodInfo OrderByAscendingMethod =
            typeof(Queryable).GetMethods()
                .Where(method => method.Name == "OrderBy")
                .Where(method => method.GetParameters().Length == 2)
                .Single();

        private static readonly MethodInfo OrderByDescendingMethod =
            typeof(Queryable).GetMethods()
                .Where(method => method.Name == "OrderByDescending")
                .Where(method => method.GetParameters().Length == 2)
                .Single();

        private static readonly MethodInfo ThenByAscendingMethod =
            typeof(Queryable).GetMethods()
                .Where(method => method.Name == "ThenBy")
                .Where(method => method.GetParameters().Length == 2)
                .Single();

        private static readonly MethodInfo ThenByDescendingMethod =
            typeof(Queryable).GetMethods()
                .Where(method => method.Name == "ThenByDescending")
                .Where(method => method.GetParameters().Length == 2)
                .Single();

        #endregion
        //================================================================
        #region Generic filters (FilterCriteria)

        public const string FilterOperationMatches = "Matches";
        public const string FilterOperationNotMatches = "NotMatches";

        private void ValidateAndPrepare(FilterCriteria filter)
        {
            if (!string.IsNullOrEmpty(filter.Property) && !string.IsNullOrEmpty(filter.Filter))
                throw new ClientException("Invalid generic filter criteria: both property filter and predefined filter are set. (Property = '" + filter.Property + "', Filter = '" + filter.Filter + "')");

            if (string.IsNullOrEmpty(filter.Property) && string.IsNullOrEmpty(filter.Filter))
                throw new ClientException("Invalid generic filter criteria: both property filter and predefined filter are null.");

            if (!string.IsNullOrEmpty(filter.Property))
            {
                // Property filter:

                if (string.IsNullOrEmpty(filter.Operation))
                    throw new ClientException("Invalid generic filter criteria: Operation is not set. (Property = '" + filter.Property + "')");
            }

            if (!string.IsNullOrEmpty(filter.Filter))
            {
                // Specific filter:

                if (string.IsNullOrEmpty(filter.Operation))
                    filter.Operation = FilterOperationMatches;
                
                if (!string.Equals(filter.Operation, FilterOperationMatches, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(filter.Operation, FilterOperationNotMatches, StringComparison.OrdinalIgnoreCase))
                    throw new ClientException(string.Format(
                        "Invalid generic filter criteria: Supported predefined filter operations are '{0}' and '{1}'. (Filter = '{2}', Operation = '{3}')",
                        FilterOperationMatches, FilterOperationNotMatches, filter.Filter, filter.Operation));
            }
        }

        private Type GetSpecificFilterType(string filterName)
        {
            if (string.IsNullOrEmpty(filterName))
                throw new ArgumentNullException("filterName");

            Type filterType = null;

            filterType = _domainObjectModel.Assembly.GetType(filterName);

            filterType = filterType ?? Type.GetType(filterName);

            if (filterType == null)
                throw new ClientException(string.Format("Unknown filter type '{0}'.", filterName));

            return filterType;
        }

        public class FilterObject
        {
            public Type FilterType;
            public object Parameter;
        }

        public IList<FilterObject> ToFilterObjects(IEnumerable<FilterCriteria> genericFilter)
        {
            CsUtility.Materialize(ref genericFilter);

            foreach (var filter in genericFilter)
                ValidateAndPrepare(filter);

            var filterObjects = new List<FilterObject>(genericFilter.Count());

            bool handledPropertyFilter = false;

            foreach (var filter in genericFilter)
            {
                if (IsPropertyFilter(filter))
                {
                    if (!handledPropertyFilter)
                    {
                        handledPropertyFilter = true;
                        filterObjects.Add(CombinePropertyFilters(genericFilter));
                    }
                }
                else
                {
                    Type filterType = GetSpecificFilterType(filter.Filter);

                    if (string.Equals(filter.Operation, FilterOperationNotMatches, StringComparison.OrdinalIgnoreCase))
                    {
                        if (ReflectionHelper<IEntity>.GetPredicateExpressionParameter(filterType) != null)
                            filter.Value = Not((LambdaExpression)filter.Value);
                        else
                            throw new FrameworkException(FilterOperationNotMatches + " operation is only allowed on a filter expression: Expression<Func<T, bool>>.");
                    }

                    filterObjects.Add(new FilterObject { FilterType = filterType, Parameter = filter.Value });
                }
            }

            return filterObjects;
        }

        private LambdaExpression Not(LambdaExpression expression)
        {
            return Expression.Lambda(Expression.Not(expression.Body), expression.Parameters);
        }

        private bool IsPropertyFilter(FilterCriteria filter)
        {
            return !string.IsNullOrEmpty(filter.Property);
        }

        private FilterObject CombinePropertyFilters(IEnumerable<FilterCriteria> genericFilter)
        {
            var propertyFilters = genericFilter.Where(IsPropertyFilter)
                .Select(fc => new PropertyFilter { Property = fc.Property, Operation = fc.Operation, Value = fc.Value })
                .ToList();

            return new FilterObject
            {
                Parameter = propertyFilters,
                FilterType = propertyFilters.GetType()
            };
        }

        /// <summary>
        /// Compares only filters without parameters.
        /// </summary>
        public static bool EqualsSimpleFilter(FilterCriteria filter, string filterName)
        {
            return filter.Filter == filterName
                && filter.Value == null
                && (string.IsNullOrEmpty(filter.Operation)
                    || string.Equals(filter.Operation, GenericFilterHelper.FilterOperationMatches, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}
