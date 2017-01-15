﻿/** 
 * Copyright (C) 2015-2017 smndtrl, golf1052
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using libsignal;
using libsignal.ecc;
using libsignal.state;
using libsignalservice.messages.multidevice;
using libsignalservice.push.exceptions;
using libsignalservice.util;
using Newtonsoft.Json;
using Strilanc.Value;
using Windows.Networking;
using Windows.Security.Cryptography;
using Windows.Web.Http;
using Windows.Web.Http.Filters;

namespace libsignalservice.push
{
    public class PushServiceSocket
    {
        private static readonly string TAG = "PushServiceSocket";

        private static readonly string CREATE_ACCOUNT_DEBUG_PATH = "/v1/accounts/test/code/{0}";
        private static readonly string CREATE_ACCOUNT_SMS_PATH = "/v1/accounts/sms/code/{0}";
        private static readonly string CREATE_ACCOUNT_VOICE_PATH = "/v1/accounts/voice/code/{0}";
        private static readonly string VERIFY_ACCOUNT_CODE_PATH = "/v1/accounts/code/{0}";
        private static readonly string VERIFY_ACCOUNT_TOKEN_PATH = "/v1/accounts/token/{0}";
        private static readonly string REGISTER_WNS_PATH = "/v1/accounts/wns/";
        private static readonly string REQUEST_TOKEN_PATH = "/v1/accounts/token";
        private static readonly string SET_ACCOUNT_ATTRIBUTES = "/v1/accounts/attributes";

        private static readonly string PREKEY_METADATA_PATH = "/v2/keys/";
        private static readonly string PREKEY_PATH = "/v2/keys/{0}";
        private static readonly string PREKEY_DEVICE_PATH = "/v2/keys/{0}/{1}";
        private static readonly string SIGNED_PREKEY_PATH = "/v2/keys/signed";

        private static readonly string PROVISIONING_CODE_PATH = "/v1/devices/provisioning/code";
        private static readonly string PROVISIONING_MESSAGE_PATH = "/v1/provisioning/{0}";
        private static readonly string DEVICE_PATH = "/v1/devices/{0}";

        private static readonly string DIRECTORY_TOKENS_PATH = "/v1/directory/tokens";
        private static readonly string DIRECTORY_VERIFY_PATH = "/v1/directory/{0}";
        private static readonly string MESSAGE_PATH = "/v1/messages/{0}";
        private static readonly string ACKNOWLEDGE_MESSAGE_PATH = "/v1/messages/{0}/{1}";
        private static readonly string RECEIPT_PATH = "/v1/receipt/{0}/{1}";
        private static readonly string ATTACHMENT_PATH = "/v1/attachments/{0}";

        private readonly SignalConnectionInformation[] signalConnectionInformation;
        private readonly CredentialsProvider credentialsProvider;
        private readonly string userAgent;

        public PushServiceSocket(SignalServiceUrl[] serviceUrls, CredentialsProvider credentialsProvider, string userAgent)
        {
            this.credentialsProvider = credentialsProvider;
            this.userAgent = userAgent;
            this.signalConnectionInformation = new SignalConnectionInformation[serviceUrls.Length];

            for (int i = 0; i < serviceUrls.Length; i++)
            {
                signalConnectionInformation[i] = new SignalConnectionInformation(serviceUrls[i]);
            }
        }

        public async Task<bool> createAccount(bool voice) //throws IOException
        {

            string path = voice ? CREATE_ACCOUNT_VOICE_PATH : CREATE_ACCOUNT_SMS_PATH;
            await makeRequest(string.Format(path, credentialsProvider.GetUser()), "GET", null);
            return true;
        }

        public async Task<bool> verifyAccountCode(string verificationCode, string signalingKey,
                                   uint registrationId, bool voice)
        {
            AccountAttributes signalingKeyEntity = new AccountAttributes(signalingKey, registrationId, voice, "DEBUG DEVICE", true);
            await makeRequest(string.Format(VERIFY_ACCOUNT_CODE_PATH, verificationCode),
                "PUT", JsonUtil.toJson(signalingKeyEntity));
            return true;
        }

        public async Task<bool> verifyAccountToken(string verificationToken, string signalingKey,
                                   uint registrationId, bool voice)
        {
            AccountAttributes signalingKeyEntity = new AccountAttributes(signalingKey, registrationId, voice, "DEBUG DEVICE", true);
            await makeRequest(string.Format(VERIFY_ACCOUNT_TOKEN_PATH, verificationToken),
                "PUT", JsonUtil.toJson(signalingKeyEntity));
            return true;
        }

        public async Task<bool> setAccountAttributes(string signalingKey, uint registrationId,
                                  bool voice, bool fetchesMessages)
        {
            AccountAttributes accountAttributesEntity = new AccountAttributes(signalingKey, registrationId, voice, "DEBUG DEVICE", fetchesMessages);
            await makeRequest(SET_ACCOUNT_ATTRIBUTES,
                "PUT", JsonUtil.toJson(accountAttributesEntity));
            return true;
        }

        public async Task<string> getAccountVerificationToken()// throws IOException
        {
            string responseText = await makeRequest(REQUEST_TOKEN_PATH, "GET", null);
            return JsonUtil.fromJson<AuthorizationToken>(responseText).Token;
        }

        public async Task<string> getNewDeviceVerificationCode()// throws IOException
        {
            string responseText = await makeRequest(PROVISIONING_CODE_PATH, "GET", null);
            return JsonUtil.fromJson<DeviceCode>(responseText).getVerificationCode();
        }

        public async Task<bool> sendProvisioningMessage(string destination, byte[] body)// throws IOException
        {
            await makeRequest(string.Format(PROVISIONING_MESSAGE_PATH, destination), "PUT",
                    JsonUtil.toJson(new ProvisioningMessage(Base64.encodeBytes(body))));
            return true;
        }

        public async Task<List<DeviceInfo>> getDevices()// throws IOException
        {
            string responseText = await makeRequest(string.Format(DEVICE_PATH, ""), "GET", null);
            return JsonUtil.fromJson<DeviceInfoList>(responseText).getDevices();
        }

        public async Task<bool> removeDevice(long deviceId)// throws IOException
        {
            await makeRequest(string.Format(DEVICE_PATH, deviceId), "DELETE", null);
            return true;
        }

        public async Task<bool> sendReceipt(string destination, ulong messageId, May<string> relay)// throws IOException
        {
            string path = string.Format(RECEIPT_PATH, destination, messageId);

            if (relay.HasValue)
            {
                path += "?relay=" + relay.ForceGetValue();
            }

            await makeRequest(path, "PUT", null);
            return true;
        }

        public async Task<bool> registerWnsId(string wnsRegistrationId)// throws IOException
        {
            WnsRegistrationId registration = new WnsRegistrationId(wnsRegistrationId);
            return await makeRequest(REGISTER_WNS_PATH, "PUT", JsonUtil.toJson(registration)) != null;
        }

        public async Task<bool> unregisterWnsId()// throws IOException
        {
            return await makeRequest(REGISTER_WNS_PATH, "DELETE", null) != null;
        }

        public async Task<SendMessageResponse> sendMessage(OutgoingPushMessageList bundle)
        //throws IOException
        {
            try
            {
                string responseText = await makeRequest(string.Format(MESSAGE_PATH, bundle.getDestination()), "PUT", JsonUtil.toJson(bundle));

                if (responseText == null) return new SendMessageResponse(false);
                else return JsonUtil.fromJson<SendMessageResponse>(responseText);
            }
            catch (Exception nfe)
            {
                throw new UnregisteredUserException(bundle.getDestination(), nfe);
            }
        }

        public async Task<List<SignalServiceEnvelopeEntity>> getMessages()// throws IOException
        {
            string responseText = await makeRequest(string.Format(MESSAGE_PATH, ""), "GET", null);
            return JsonUtil.fromJson<SignalServiceEnvelopeEntityList>(responseText).getMessages();
        }

        public async Task<bool> acknowledgeMessage(string sender, ulong timestamp)// throws IOException
        {
            await makeRequest(string.Format(ACKNOWLEDGE_MESSAGE_PATH, sender, timestamp), "DELETE", null);
            return true;
        }

        public async Task<bool> registerPreKeys(IdentityKey identityKey,
                                    PreKeyRecord lastResortKey,
                                    SignedPreKeyRecord signedPreKey,
                                    IList<PreKeyRecord> records)
        //throws IOException
        {
            List<PreKeyEntity> entities = new List<PreKeyEntity>();

            foreach (PreKeyRecord record in records)
            {
                PreKeyEntity entity = new PreKeyEntity(record.getId(),
                                                       record.getKeyPair().getPublicKey());

                entities.Add(entity);
            }

            PreKeyEntity lastResortEntity = new PreKeyEntity(lastResortKey.getId(),
                                                     lastResortKey.getKeyPair().getPublicKey());

            SignedPreKeyEntity signedPreKeyEntity = new SignedPreKeyEntity(signedPreKey.getId(),
                                                                   signedPreKey.getKeyPair().getPublicKey(),
                                                                   signedPreKey.getSignature());

            await makeRequest(string.Format(PREKEY_PATH, ""), "PUT",
                JsonUtil.toJson(new PreKeyState(entities, lastResortEntity,
                                                signedPreKeyEntity, identityKey)));
            return true;
        }

        public async Task<int> getAvailablePreKeys()// throws IOException
        {
            string responseText = await makeRequest(PREKEY_METADATA_PATH, "GET", null);
            PreKeyStatus preKeyStatus = JsonUtil.fromJson<PreKeyStatus>(responseText);

            return preKeyStatus.getCount();
        }

        public async Task<List<PreKeyBundle>> getPreKeys(SignalServiceAddress destination, uint deviceIdInteger)// throws IOException
        {
            try
            {
                string deviceId = deviceIdInteger.ToString();

                if (deviceId.Equals("1"))
                    deviceId = "*";

                string path = string.Format(PREKEY_DEVICE_PATH, destination.getNumber(), deviceId);

                if (destination.getRelay().HasValue)
                {
                    path = path + "?relay=" + destination.getRelay().ForceGetValue();
                }

                string responseText = await makeRequest(path, "GET", null);
                PreKeyResponse response = JsonUtil.fromJson<PreKeyResponse>(responseText);
                List<PreKeyBundle> bundles = new List<PreKeyBundle>();

                foreach (PreKeyResponseItem device in response.getDevices())
                {
                    ECPublicKey preKey = null;
                    ECPublicKey signedPreKey = null;
                    byte[] signedPreKeySignature = null;
                    int preKeyId = -1;
                    int signedPreKeyId = -1;

                    if (device.getSignedPreKey() != null)
                    {
                        signedPreKey = device.getSignedPreKey().getPublicKey();
                        signedPreKeyId = (int)device.getSignedPreKey().getKeyId(); // TODO: whacky
                        signedPreKeySignature = device.getSignedPreKey().getSignature();
                    }

                    if (device.getPreKey() != null)
                    {
                        preKeyId = (int)device.getPreKey().getKeyId();// TODO: whacky
                        preKey = device.getPreKey().getPublicKey();
                    }

                    bundles.Add(new PreKeyBundle(device.getRegistrationId(), device.getDeviceId(), (uint)preKeyId,
                                                         preKey, (uint)signedPreKeyId, signedPreKey, signedPreKeySignature,
                                                         response.getIdentityKey()));// TODO: whacky
                }

                return bundles;
            }
            /*catch (JsonUtil.JsonParseException e)
            {
                throw new IOException(e);
            }*/
            catch (NotFoundException nfe)
            {
                throw new UnregisteredUserException(destination.getNumber(), nfe);
            }
        }

        public async Task<PreKeyBundle> getPreKey(SignalServiceAddress destination, uint deviceId)// throws IOException
        {
            try
            {
                string path = string.Format(PREKEY_DEVICE_PATH, destination.getNumber(),
                                            deviceId.ToString());

                if (destination.getRelay().HasValue)
                {
                    path = path + "?relay=" + destination.getRelay().ForceGetValue();
                }

                string responseText = await makeRequest(path, "GET", null);
                PreKeyResponse response = JsonUtil.fromJson<PreKeyResponse>(responseText);

                if (response.getDevices() == null || response.getDevices().Count < 1)
                    throw new Exception("Empty prekey list");

                PreKeyResponseItem device = response.getDevices()[0];
                ECPublicKey preKey = null;
                ECPublicKey signedPreKey = null;
                byte[] signedPreKeySignature = null;
                int preKeyId = -1;
                int signedPreKeyId = -1;

                if (device.getPreKey() != null)
                {
                    preKeyId = (int)device.getPreKey().getKeyId();// TODO: whacky
                    preKey = device.getPreKey().getPublicKey();
                }

                if (device.getSignedPreKey() != null)
                {
                    signedPreKeyId = (int)device.getSignedPreKey().getKeyId();// TODO: whacky
                    signedPreKey = device.getSignedPreKey().getPublicKey();
                    signedPreKeySignature = device.getSignedPreKey().getSignature();
                }

                return new PreKeyBundle(device.getRegistrationId(), device.getDeviceId(), (uint)preKeyId, preKey,
                                        (uint)signedPreKeyId, signedPreKey, signedPreKeySignature, response.getIdentityKey());
            }
            /*catch (JsonUtil.JsonParseException e)
            {
                throw new IOException(e);
            }*/
            catch (NotFoundException nfe)
            {
                throw new UnregisteredUserException(destination.getNumber(), nfe);
            }
        }

        public async Task<SignedPreKeyEntity> getCurrentSignedPreKey()// throws IOException
        {
            try
            {
                string responseText = await makeRequest(SIGNED_PREKEY_PATH, "GET", null);
                return JsonUtil.fromJson<SignedPreKeyEntity>(responseText);
            }
            catch (/*NotFound*/Exception e)
            {
                Debug.WriteLine(e.Message, TAG);
                return null;
            }
        }

        public async Task<bool> setCurrentSignedPreKey(SignedPreKeyRecord signedPreKey)// throws IOException
        {
            SignedPreKeyEntity signedPreKeyEntity = new SignedPreKeyEntity(signedPreKey.getId(),
                                                                           signedPreKey.getKeyPair().getPublicKey(),
                                                                           signedPreKey.getSignature());
            await makeRequest(SIGNED_PREKEY_PATH, "PUT", JsonUtil.toJson(signedPreKeyEntity));
            return true;
        }

        public async Task<ulong> sendAttachment(PushAttachmentData attachment)// throws IOException
        {
            string response = await makeRequest(string.Format(ATTACHMENT_PATH, ""), "GET", null);
            AttachmentDescriptor attachmentKey = JsonUtil.fromJson<AttachmentDescriptor>(response);

            if (attachmentKey == null || attachmentKey.getLocation() == null)
            {
                throw new Exception("Server failed to allocate an attachment key!");
            }

            Debug.WriteLine("Got attachment content location: " + attachmentKey.getLocation(), TAG);

            /*uploadAttachment("PUT", attachmentKey.getLocation(), attachment.getData(),
                             attachment.getDataSize(), attachment.getKey());
*/
            throw new NotImplementedException("PushServiceSocket sendAttachment");
            return attachmentKey.getId();
        }
        /*
        public void retrieveAttachment(string relay, long attachmentId, File destination)// throws IOException
        {
            string path = string.Format(ATTACHMENT_PATH, attachmentId.ToString());

            if (!Util.isEmpty(relay))
            {
                path = path + "?relay=" + relay;
            }

            string response = makeRequest(path, "GET", null);
            //AttachmentDescriptor descriptor = JsonUtil.fromJson(response, AttachmentDescriptor.class);

            //Log.w(TAG, "Attachment: " + attachmentId + " is at: " + descriptor.getLocation());

            //downloadExternalFile(descriptor.getLocation(), destination);

            throw new NotImplementedException();
        }*/

        public async Task<List<ContactTokenDetails>> retrieveDirectory(ICollection<string> contactTokens) // TODO: whacky
                                                                                              //throws NonSuccessfulResponseCodeException, PushNetworkException
        {
            LinkedList<HashSet<string>> temp = new LinkedList<HashSet<string>>();
            ContactTokenList contactTokenList = new ContactTokenList(contactTokens.ToList());
            string response = await makeRequest(DIRECTORY_TOKENS_PATH, "PUT", JsonUtil.toJson(contactTokenList));
            ContactTokenDetailsList activeTokens = JsonUtil.fromJson<ContactTokenDetailsList>(response);

            return activeTokens.getContacts();
        }

        public async Task<ContactTokenDetails> getContactTokenDetails(string contactToken)// throws IOException
        {
            try
            {
                string response = await makeRequest(string.Format(DIRECTORY_VERIFY_PATH, contactToken), "GET", null);
                return JsonUtil.fromJson<ContactTokenDetails>(response);
            }
            catch (/*NotFound*/Exception nfe)
            {
                return null;
            }
        }
        /*
        private void downloadExternalFile(string url, File localDestination)
        //throws IOException
        {
            URL downloadUrl = new URL(url);
            HttpURLConnection connection = (HttpURLConnection)downloadUrl.openConnection();
            connection.setRequestProperty("Content-Type", "application/octet-stream");
            connection.setRequestMethod("GET");
            connection.setDoInput(true);

            try
            {
                if (connection.getResponseCode() != 200)
                {
                    throw new NonSuccessfulResponseCodeException("Bad response: " + connection.getResponseCode());
                }

                OutputStream output = new FileOutputStream(localDestination);
                InputStream input = connection.getInputStream();
                byte[] buffer = new byte[4096];
                int read;

                while ((read = input.read(buffer)) != -1)
                {
                    output.write(buffer, 0, read);
                }

                output.close();
                Log.w(TAG, "Downloaded: " + url + " to: " + localDestination.getAbsolutePath());
            }
            catch (IOException ioe)
            {
                throw new PushNetworkException(ioe);
            }
            finally
            {
                connection.disconnect();
            }
        }*/

        /*
        private void uploadAttachment(string method, string url, InputStream data, long dataSize, byte[] key)
        //throws IOException
        {
            URL uploadUrl = new URL(url);
            HttpsURLConnection connection = (HttpsURLConnection)uploadUrl.openConnection();
            connection.setDoOutput(true);

            if (dataSize > 0)
            {
                connection.setFixedLengthStreamingMode((int)AttachmentCipherOutputStream.getCiphertextLength(dataSize));
            }
            else
            {
                connection.setChunkedStreamingMode(0);
            }

            connection.setRequestMethod(method);
            connection.setRequestProperty("Content-Type", "application/octet-stream");
            connection.setRequestProperty("Connection", "close");
            connection.connect();

            try
            {
                OutputStream stream = connection.getOutputStream();
                AttachmentCipherOutputStream out    = new AttachmentCipherOutputStream(key, stream);

                Util.copy(data, out);
      out.flush();

                if (connection.getResponseCode() != 200)
                {
                    throw new IOException("Bad response: " + connection.getResponseCode() + " " + connection.getResponseMessage());
                }
            }
            finally
            {
                connection.disconnect();
            }
        }*/

        private async Task<string> makeRequest(string urlFragment, string method, string body)
        //throws NonSuccessfulResponseCodeException, PushNetworkException
        {
            try
            {
                string response = await makeBaseRequest(urlFragment, method, body);

                return response;
            }
            catch (Exception ioe)
            {
                throw new PushNetworkException(ioe);
            }
        }

        private async Task<string> makeBaseRequest(string urlFragment, string method, string body)
        {
            HttpResponseMessage connection = await getConnection(urlFragment, method, body);
            HttpStatusCode responseCode;
            string responseMessage;
            string response;

            try
            {
                responseCode = connection.StatusCode;
                responseMessage = await connection.Content.ReadAsStringAsync();
            }
            catch (Exception ioe)
            {
                throw new PushNetworkException(ioe);
            }

            switch (responseCode)
            {
                case HttpStatusCode.RequestEntityTooLarge: // 413
                    throw new RateLimitException("Rate limit exceeded: " + responseCode);
                case HttpStatusCode.Unauthorized: // 401
                case HttpStatusCode.Forbidden: // 403
                    throw new AuthorizationFailedException("Authorization failed!");
                case HttpStatusCode.NotFound: // 404
                    throw new NotFoundException("Not found");
                case HttpStatusCode.Conflict: // 409
                    try
                    {
                        response = await connection.Content.ReadAsStringAsync();
                    }
                    catch (/*IO*/Exception e)
                    {
                        throw new PushNetworkException(e);
                    }
                    throw new MismatchedDevicesException(JsonUtil.fromJson<MismatchedDevices>(response));
                case HttpStatusCode.Gone: // 410
                    try
                    {
                        response = await connection.Content.ReadAsStringAsync();  //Util.readFully(connection.getErrorStream());
                    }
                    catch (/*IO*/Exception e)
                    {
                        throw new PushNetworkException(e);
                    }
                    throw new StaleDevicesException(JsonUtil.fromJson<StaleDevices>(response));
                case HttpStatusCode.LengthRequired://411:
                    try
                    {
                        response = await connection.Content.ReadAsStringAsync();  //Util.readFully(connection.getErrorStream());
                    }
                    catch (Exception e)
                    {
                        throw new PushNetworkException(e);
                    }
                    throw new DeviceLimitExceededException(JsonUtil.fromJson<DeviceLimit>(response));
                case HttpStatusCode.ExpectationFailed: // 417
                    throw new ExpectationFailedException();

            }

            if (responseCode != HttpStatusCode.Ok && responseCode != HttpStatusCode.NoContent) // 200 & 204
            {
                throw new NonSuccessfulResponseCodeException("Bad response: " + (int)responseCode + " " +
                                                             responseMessage);
            }

            response = await connection.Content.ReadAsStringAsync();
            return response;
        }

        private async Task<HttpResponseMessage> getConnection(string urlFragment, string method, string body)
        {
            try
            {
                SignalConnectionInformation connectionInformation = getRandom(signalConnectionInformation);
                string url = connectionInformation.getUrl();
                May<string> hostHeader = connectionInformation.getHostHeader();
                //TrustManger[] trustManagers = connectionInformation.getTrustManagers();

                /*SSLContext context = SSLContext.getInstance("TLS");
                context.init(null, trustManagers, null);*/

                Uri uri = new Uri(string.Format("{0}{1}", url, urlFragment));
                //Log.w(TAG, "Push service URL: " + serviceUrl);
                //Log.w(TAG, "Opening URL: " + url);
                var filter = new HttpBaseProtocolFilter();

                if (HttpClientCertificatePolicyState.Policy == HttpClientCertificatePolicy.DevelopmentMode)
                {
                    filter.IgnorableServerCertificateErrors.Add(Windows.Security.Cryptography.Certificates.ChainValidationResult.Expired);
                    filter.IgnorableServerCertificateErrors.Add(Windows.Security.Cryptography.Certificates.ChainValidationResult.Untrusted);
                    filter.IgnorableServerCertificateErrors.Add(Windows.Security.Cryptography.Certificates.ChainValidationResult.InvalidName);
                }

                //HttpURLConnection connection = (HttpURLConnection)url.openConnection();

                HttpClient connection = new HttpClient(filter);

                /*if (ENFORCE_SSL)
                {
                    ((HttpsURLConnection)connection).setSSLSocketFactory(context.getSocketFactory());
                    ((HttpsURLConnection)connection).setHostnameVerifier(new StrictHostnameVerifier());
                }*/

                var headers = connection.DefaultRequestHeaders;

                //connection.setRequestMethod(method);
                //headers.Add("Content-Type", "application/json");

                if (credentialsProvider.GetPassword() != null)
                {
                    headers.Add("Authorization", getAuthorizationHeader());
                }

                if (userAgent != null)
                {
                    headers.Add("X-Signal-Agent", userAgent);
                }

                if (hostHeader.HasValue)
                {
                    headers.Host = new HostName(hostHeader.ForceGetValue());
                }

                /*if (body != null)
                {
                    connection.setDoOutput(true);
                }*/

                //connection.connect();

                HttpStringContent content;
                if (body != null)
                {
                    content = new HttpStringContent(body, Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");
                    Debug.WriteLine(body);
                }
                else
                {
                    content = new HttpStringContent("");
                }
                switch (method)
                {
                    case "POST":
                        return await connection.PostAsync(uri, content);
                    case "PUT":
                        return await connection.PutAsync(uri, content);
                    case "DELETE":
                        return await connection.DeleteAsync(uri);
                    case "GET":
                        return await connection.GetAsync(uri);
                    default:
                        throw new Exception("Unknown method: " + method);
                }

            }
            catch (UriFormatException e)
            {
                throw new Exception(string.Format("Uri {0} {1} is wrong", "", urlFragment));
            }
            catch (Exception e)
            {
                Debug.WriteLine(string.Format("Other exception {0}{1} is wrong", "", urlFragment));
                throw new PushNetworkException(e);
            }

        }


        private string getAuthorizationHeader()
        {
            try
            {
                return "Basic " + Base64.encodeBytes(Encoding.UTF8.GetBytes((credentialsProvider.GetUser() + ":" + credentialsProvider.GetPassword())));
            }
            catch (/*UnsupportedEncoding*/Exception e)
            {
                throw new Exception(e.Message);
            }
        }

        private SignalConnectionInformation getRandom(SignalConnectionInformation[] connections)
        {
            return connections[CryptographicBuffer.GenerateRandomNumber() % connections.Length];
        }
    }


    class WnsRegistrationId
    {

        [JsonProperty]
        private string wnsRegistrationId;

        /*[JsonProperty]
        private bool webSocketChannel;*/

        public WnsRegistrationId() { }

        public WnsRegistrationId(string wnsRegistrationId)
        {
            this.wnsRegistrationId = wnsRegistrationId;
            //this.webSocketChannel = webSocketChannel;
        }
    }

    class AttachmentDescriptor
    {
        [JsonProperty]
        private ulong id;

        [JsonProperty]
        private string location;

        public ulong getId()
        {
            return id;
        }

        public string getLocation()
        {
            return location;
        }
    }

    class SignalConnectionInformation
    {
        private readonly string url;
        private readonly May<string> hostHeader;
        //private readonly TrustManager[] trustManagers;

        public SignalConnectionInformation(SignalServiceUrl signalServiceUrl)
        {
            this.url = signalServiceUrl.getUrl();
            this.hostHeader = signalServiceUrl.getHostHeader();
            //this.trustManagers = BlacklistingTrustManager.createFor(signalServiceUrl.getTrustStore());
        }

        public string getUrl()
        {
            return url;
        }

        public May<string> getHostHeader()
        {
            return hostHeader;
        }

        //TrustManager[] getTrustManagers()
        //{
        //    return trustManagers;
        //}

        //public May<ConnectionSpec> getConnectionSpec()
        //{
        //    return connectionSpec;
        //}
    }
}

