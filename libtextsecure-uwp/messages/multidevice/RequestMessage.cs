﻿/** 
 * Copyright (C) 2015 smndtrl
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using libtextsecure.push;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static libtextsecure.push.TextSecureProtos.ContactDetails.Types;
using static libtextsecure.push.TextSecureProtos.SyncMessage.Types;

namespace libtextsecure.messages.multidevice
{
    public class RequestMessage
    {

        private readonly Request request;

        public RequestMessage(Request request)
        {
            this.request = request;
        }

        public bool isContactsRequest()
        {
            return request.Type == Request.Types.Type.CONTACTS;
        }

        public bool isGroupsRequest()
        {
            return request.Type == Request.Types.Type.GROUPS;
        }
    }
}
