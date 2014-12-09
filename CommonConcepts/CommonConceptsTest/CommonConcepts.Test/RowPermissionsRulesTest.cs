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

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rhetos;
using Rhetos.Configuration.Autofac;
using Rhetos.Dom.DefaultConcepts;
using Rhetos.Processing.DefaultCommands;
using Rhetos.TestCommon;
using Rhetos.Utilities;
using System;
using System.Linq;
using TestRowPermissions;

namespace CommonConcepts.Test
{
    [TestClass]
    public class RowPermissionsRulesTest
    {
        [TestMethod]
        public void FilterNoPermissions()
        {
            using (var container = new RhetosTestContainer())
            {
                var repositories = container.Resolve<Common.DomRepository>();
                var itemsRepository = repositories.TestRowPermissions.RPRulesItem;
                var groupsRepository = repositories.TestRowPermissions.RPRulesGroup;
                itemsRepository.Delete(itemsRepository.All());
                groupsRepository.Delete(groupsRepository.All());

                var g1 = new RPRulesGroup { ID = Guid.NewGuid(), Name = "g1" };
                var g2 = new RPRulesGroup { ID = Guid.NewGuid(), Name = "g2" };
                var i1 = new RPRulesItem { ID = Guid.NewGuid(), Name = "i1", Group = g1 };
                var i2 = new RPRulesItem { ID = Guid.NewGuid(), Name = "i2", Group = g1 };
                var i3 = new RPRulesItem { ID = Guid.NewGuid(), Name = "i3", Group = g2 };
                var i4 = new RPRulesItem { ID = Guid.NewGuid(), Name = "i4", Group = g2 };

                groupsRepository.Insert(new[] { g1, g2 });
                itemsRepository.Insert(new[] { i1, i2, i3, i4 });

                var allowedItems = itemsRepository.Filter(itemsRepository.Query(), new Common.RowPermissionsReadItems());
                Console.WriteLine(itemsRepository.Query().Expression.ToString());
                Console.WriteLine(allowedItems.Expression.ToString());
                Assert.AreEqual("", TestUtility.DumpSorted(allowedItems, item => item.Name));
                Assert.AreEqual("TestRowPermissions.RPRulesItem[]", allowedItems.Expression.ToString(), "No need for query, an empty array should be returned.");
            }
        }

        [TestMethod]
        public void FilterWithPermissions()
        {
            using (var container = new RhetosTestContainer())
            {
                var currentUserName = container.Resolve<IUserInfo>().UserName;
                var repositories = container.Resolve<Common.DomRepository>();
                var itemsRepository = repositories.TestRowPermissions.RPRulesItem;
                var groupsRepository = repositories.TestRowPermissions.RPRulesGroup;
                itemsRepository.Delete(itemsRepository.All());
                groupsRepository.Delete(groupsRepository.All());

                var g1 = new RPRulesGroup { ID = Guid.NewGuid(), Name = "g1" };
                var g2 = new RPRulesGroup { ID = Guid.NewGuid(), Name = "g2" };
                var i1 = new RPRulesItem { ID = Guid.NewGuid(), Name = "i1", Group = g1 };
                var i2 = new RPRulesItem { ID = Guid.NewGuid(), Name = "i2", Group = g1 };
                var i3 = new RPRulesItem { ID = Guid.NewGuid(), Name = "i3", Group = g2 };
                var i4 = new RPRulesItem { ID = Guid.NewGuid(), Name = "i4", Group = g2 };

                groupsRepository.Insert(new[] { g1, g2 });
                itemsRepository.Insert(new[] { i1, i2, i3, i4 });

                repositories.TestRowPermissions.RpRulesAllowGroup.Insert(new[] {
                    new TestRowPermissions.RpRulesAllowGroup { UserName = currentUserName, Group = g2 } });

                repositories.TestRowPermissions.RpRulesAllowItem.Insert(new[] {
                    new TestRowPermissions.RpRulesAllowItem { UserName = currentUserName, Item = i2 } });

                repositories.TestRowPermissions.RpRulesDenyItem.Insert(new[] {
                    new TestRowPermissions.RpRulesDenyItem { UserName = currentUserName, Item = i3 } });

                var allowedItems = itemsRepository.Filter(itemsRepository.Query(), new Common.RowPermissionsReadItems());
                Console.WriteLine(itemsRepository.Query().Expression.ToString());
                Console.WriteLine(allowedItems.Expression.ToString());
                Assert.AreEqual("i2, i4", TestUtility.DumpSorted(allowedItems, item => item.Name));
            }
        }

        [TestMethod]
        public void FilterOptimizeAllowedAll()
        {
            using (var container = new RhetosTestContainer())
            {
                var currentUserName = container.Resolve<IUserInfo>().UserName;
                var repositories = container.Resolve<Common.DomRepository>();
                var itemsRepository = repositories.TestRowPermissions.RPRulesItem;
                var groupsRepository = repositories.TestRowPermissions.RPRulesGroup;
                itemsRepository.Delete(itemsRepository.All());
                groupsRepository.Delete(groupsRepository.All());

                var g1 = new RPRulesGroup { ID = Guid.NewGuid(), Name = "g1" };
                var g2 = new RPRulesGroup { ID = Guid.NewGuid(), Name = "g2" };
                var i1 = new RPRulesItem { ID = Guid.NewGuid(), Name = "i1", Group = g1 };
                var i2 = new RPRulesItem { ID = Guid.NewGuid(), Name = "i2", Group = g1 };
                var i3 = new RPRulesItem { ID = Guid.NewGuid(), Name = "i3", Group = g2 };
                var i4 = new RPRulesItem { ID = Guid.NewGuid(), Name = "i4", Group = g2 };

                groupsRepository.Insert(new[] { g1, g2 });
                itemsRepository.Insert(new[] { i1, i2, i3, i4 });

                repositories.TestRowPermissions.RpRulesAllowGroup.Insert(new[] {
                    new TestRowPermissions.RpRulesAllowGroup { UserName = currentUserName, Group = g1 },
                    new TestRowPermissions.RpRulesAllowGroup { UserName = currentUserName, Group = g2 } });

                repositories.TestRowPermissions.RpRulesAllowItem.Insert(new[] {
                    new TestRowPermissions.RpRulesAllowItem { UserName = currentUserName, Item = i1 },
                    new TestRowPermissions.RpRulesAllowItem { UserName = currentUserName, Item = i2 } });

                var allowedItems = itemsRepository.Filter(itemsRepository.Query(), new Common.RowPermissionsReadItems());
                Console.WriteLine(itemsRepository.Query().Expression.ToString());
                Console.WriteLine(allowedItems.Expression.ToString());
                Assert.AreEqual("i1, i2, i3, i4", TestUtility.DumpSorted(allowedItems, item => item.Name));
                Assert.AreEqual(itemsRepository.Query().Expression.ToString(), allowedItems.Expression.ToString(), "'AllowedAllGroups' rule should result with an optimized query without the 'where' part.");
            }
        }

        [TestMethod]
        public void InheritFrom()
        {
            using (var container = new RhetosTestContainer())
            {
                Parent
                    pReadAllow = new Parent() { ID = Guid.NewGuid(), value = 190 },
                    pReadDeny = new Parent() { ID = Guid.NewGuid(), value = 90 },
                    pWriteAllow = new Parent() { ID = Guid.NewGuid(), value = 60 },
                    pWriteDeny = new Parent() { ID = Guid.NewGuid(), value = 160 };

                Child
                    cParentReadAllow = new Child() { ID = Guid.NewGuid(), MyParentID = pReadAllow.ID, value = 5 },
                    cParentReadDeny = new Child() { ID = Guid.NewGuid(), MyParentID = pReadDeny.ID, value = 6 },
                    cParentWriteAllow = new Child() { ID = Guid.NewGuid(), MyParentID = pWriteAllow.ID, value = 7 },
                    cParentWriteDeny = new Child() { ID = Guid.NewGuid(), MyParentID = pWriteDeny.ID, value = 8 };

                var repositories = container.Resolve<Common.DomRepository>();
                var parentRepo = repositories.TestRowPermissions.Parent;
                var childRepo = repositories.TestRowPermissions.Child;
                var babyRepo = repositories.TestRowPermissions.Baby;

                parentRepo.Delete(parentRepo.All());
                childRepo.Delete(childRepo.All());
                babyRepo.Delete(babyRepo.All());

                parentRepo.Insert(new Parent[] { pReadAllow, pReadDeny, pWriteAllow, pWriteDeny });
                childRepo.Insert(new Child[] { cParentReadAllow, cParentReadDeny, cParentWriteAllow, cParentWriteDeny });
                
                {
                    var childAllowRead = childRepo.Filter(childRepo.Query(), new Common.RowPermissionsReadItems()).ToList();
                    Assert.AreEqual("5, 8", TestUtility.DumpSorted(childAllowRead, a => a.value.ToString()));
                }

                {
                    var childAllowWrite = childRepo.Filter(childRepo.Query(), new Common.RowPermissionsWriteItems()).ToList();
                    Assert.AreEqual("6, 7", TestUtility.DumpSorted(childAllowWrite, a => a.value.ToString()));
                }

                // Test combination with rule on child
                Child cCombo = new Child() { ID = Guid.NewGuid(), MyParentID = pReadAllow.ID, value = 3 };
                childRepo.Insert(new Child[] { cCombo });
                {
                    var childAllowRead = childRepo.Filter(childRepo.Query(), new Common.RowPermissionsReadItems()).ToList();
                    Assert.IsTrue(!childAllowRead.Select(a => a.value).Contains(3));
                }

                // Test double inheritance, only write deny case
                Baby bDenyWrite = new Baby() { ID = Guid.NewGuid(), MyParentID = cParentWriteDeny.ID };
                babyRepo.Insert(new Baby[] { bDenyWrite });
                {
                    Assert.AreEqual(1, babyRepo.Query().Count());
                    var babyDenyWrite = babyRepo.Filter(babyRepo.Query(), new Common.RowPermissionsWriteItems()).ToList();
                    Assert.AreEqual(0, babyDenyWrite.Count());
                }
            }
        }
    }
}
