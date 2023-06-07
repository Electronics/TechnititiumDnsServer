/*
Technitium DNS Server
Copyright (C) 2023  Shreyas Zare (shreyas@technitium.com)

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.

*/

using DnsServerCore.ApplicationCommon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using TechnitiumLibrary.Net;
using TechnitiumLibrary.Net.Dns;
using TechnitiumLibrary.Net.Dns.ResourceRecords;

namespace AutoPtrBetter
{
    public class App : IDnsApplication, IDnsAppRecordRequestHandler
    {
        IDnsServer _dnsServer;

        #region IDisposable

        public void Dispose()
        {
            //do nothing
        }

        #endregion

        #region public

        public Task InitializeAsync(IDnsServer dnsServer, string config)
        {
            _dnsServer = dnsServer;
            return Task.CompletedTask;
        }

        public Task<DnsDatagram> ProcessRequestAsync(DnsDatagram request, IPEndPoint remoteEP, DnsTransportProtocol protocol, bool isRecursionAllowed, string zoneName, string appRecordName, uint appRecordTtl, string appRecordData)
        {
            DnsQuestionRecord question = request.Question[0];

            if (question.Type != DnsResourceRecordType.PTR)
                return Task.FromResult<DnsDatagram>(null);

            string qname = question.Name;

            if (qname.Length == appRecordName.Length)
                return Task.FromResult<DnsDatagram>(null);

            if (!IPAddressExtensions.TryParseReverseDomain(qname, out IPAddress address))
                return Task.FromResult<DnsDatagram>(null);

            string zone = null;
            string foundDomain = null;

            using (JsonDocument jsonDocument = JsonDocument.Parse(appRecordData))
            {
                JsonElement jsonAppRecordData = jsonDocument.RootElement;

				if (jsonAppRecordData.TryGetProperty("zone", out JsonElement jsonSuffix) && (jsonSuffix.ValueKind != JsonValueKind.Null))
					zone = jsonSuffix.GetString();

				if (String.IsNullOrEmpty(zone)) {
                    _dnsServer.WriteLog("Empty zone in config!");
					return Task.FromResult<DnsDatagram>(null);
				}

                List<DnsResourceRecord> records = new List<DnsResourceRecord>();
                _dnsServer.ListAllZoneRecords(zone, records);

                var result = records.Find(x =>
                    (x.Type == DnsResourceRecordType.A || x.Type == DnsResourceRecordType.AAAA) &&
                    x.RDATA.ToString() == address.ToString());

                if (result==null) {
					return Task.FromResult<DnsDatagram>(null);
				}

                foundDomain = result.Name;
            }

            DnsResourceRecord[] answer = new DnsResourceRecord[] { new DnsResourceRecord(qname, DnsResourceRecordType.PTR, DnsClass.IN, appRecordTtl, new DnsPTRRecordData(foundDomain)) };

            return Task.FromResult(new DnsDatagram(request.Identifier, true, request.OPCODE, true, false, request.RecursionDesired, isRecursionAllowed, false, false, DnsResponseCode.NoError, request.Question, answer));
        }

        #endregion

        #region properties

        public string Description
        { get { return "Returns automatically generated response for a PTR request for both IPv4 and IPv6 given existing A/AAAA records from a specified domain."; } }

        public string ApplicationRecordDataTemplate
        {
            get
            {
                return @"{
  ""zone"": ""example.com""
}";
            }
        }

        #endregion
    }
}
