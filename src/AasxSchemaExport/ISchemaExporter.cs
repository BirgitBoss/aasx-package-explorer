﻿/*
Copyright (c) 2022 PHOENIX CONTACT GmbH & Co. KG <info@phoenixcontact.de>

This source code is licensed under the Apache License 2.0 (see LICENSE.txt).

This source code may use other Open Source software components (see LICENSE.txt).
*/

using AdminShellNS;

namespace AasxSchemaExport
{
    public interface ISchemaExporter
    {
        string ExportSchema(AdminShellV20.Submodel submodel);
    }
}
