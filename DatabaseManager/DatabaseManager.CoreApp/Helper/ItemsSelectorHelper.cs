﻿using DatabaseInterpreter.Model;
using DatabaseManager.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static Dapper.SqlMapper;

namespace DatabaseManager.Helper
{
    public class CheckItemInfo
    {
        public string Name { get; set; }
        public bool Checked { get; set; }
    }

    public class ItemsSelectorHelper
    {
        public static List<CheckItemInfo> GetDatabaseObjectTypeItems(DatabaseType databaseType)
        {
            List<DatabaseObjectType> dbObjTypes = new List<DatabaseObjectType>()
            {
                DatabaseObjectType.Trigger,
                DatabaseObjectType.Table,
                DatabaseObjectType.View,
                DatabaseObjectType.Function,
                DatabaseObjectType.Procedure,
                DatabaseObjectType.Type,
                DatabaseObjectType.Sequence
            };            

            return dbObjTypes.Select(item => new CheckItemInfo() { Name = ManagerUtil.GetPluralString(item.ToString()), Checked = true }).ToList();
        }

        public static DatabaseObjectType GetDatabaseObjectTypeByCheckItems(List<CheckItemInfo> items)
        {
            DatabaseObjectType databaseObjectType = DatabaseObjectType.None;

            foreach (var item in items)
            {
                DatabaseObjectType type = (DatabaseObjectType)Enum.Parse(typeof(DatabaseObjectType), ManagerUtil.GetSingularString(item.Name));

                databaseObjectType = databaseObjectType | type;
            }

            return databaseObjectType;
        }

        public static List<CheckItemInfo> GetDatabaseTypeItems(List<string> databaseTypes, bool checkedIfNotConfig = true)
        {
            List<CheckItemInfo> items = new List<CheckItemInfo>();

            var dbTypes = Enum.GetNames(typeof(DatabaseType));

            foreach (string dbType in dbTypes)
            {
                if (dbType != nameof(DatabaseType.Unknown))
                {
                    bool @checked = (checkedIfNotConfig && databaseTypes.Count == 0) || databaseTypes.Contains(dbType);

                    items.Add(new CheckItemInfo() { Name = dbType, Checked = @checked });
                }
            }

            return items;
        }
    }
}
