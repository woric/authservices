﻿using FluentAssertions;
using Kentor.AuthServices.Tests.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using System;
using System.Collections.Specialized;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Web;
using System.Xml;
using System.Linq;
using System.Collections.Generic;
using Kentor.AuthServices.Configuration;
using Kentor.AuthServices.WebSso;
using System.Reflection;
using System.Configuration;
using Kentor.AuthServices.Exceptions;
using System.IdentityModel.Metadata;
using System.IdentityModel.Tokens;
using Kentor.AuthServices.Tests.WebSSO;

namespace Kentor.AuthServices.Tests.WebSso
{
    [TestClass]
    public class AcsCommandTests
    {
        [TestMethod]
        public void AcsCommand_Run_NullCheckRequest()
        {
            Action a = () => new AcsCommand().Run(null, new Options(null));

            // Verify exception is thrown and that it is thrown directly by the Run()
            // method and not by some method being called by Run().
            a.ShouldThrow<ArgumentNullException>()
                .Where(e => e.ParamName == "request")
                .Where(e => e.TargetSite.Name == "Run");
        }

        [TestMethod]
        public void AcsCommand_Run_NullCheckOptions()
        {
            Action a = () => new AcsCommand().Run(new HttpRequestData("GET", new Uri("http://localhost")), null);

            a.ShouldThrow<ArgumentNullException>().And.ParamName.Should().Be("options");
        }

        [TestMethod]
        public void AcsCommand_Run_ErrorOnNoSamlResponseFound()
        {
            Action a = () => new AcsCommand().Run(
                new HttpRequestData("GET", new Uri("http://localhost")),
                Options.FromConfiguration);

            a.ShouldThrow<NoSamlResponseFoundException>()
                .WithMessage("No Saml2 Response found in the http request.");
        }

        [TestMethod]
        public void AcsCommand_Run_ErrorOnNotBase64InFormResponse()
        {
            var r = new HttpRequestData(
                "POST",
                new Uri("http://localhost"),
                "/ModulePath",
                new KeyValuePair<string, string[]>[]
                {
                    new KeyValuePair<string, string[]>("SAMLResponse", new string[] { "#¤!2" })
                },
                Enumerable.Empty<KeyValuePair<string, string>>(),
                null);

            Action a = () => new AcsCommand().Run(r, Options.FromConfiguration);

            a.ShouldThrow<BadFormatSamlResponseException>()
                .WithMessage("The SAML Response did not contain valid BASE64 encoded data.")
                .WithInnerException<FormatException>();
        }

        [TestMethod]
        public void AcsCommand_Run_ErrorOnIncorrectXml()
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("<foo />"));
            var r = new HttpRequestData(
                "POST",
                new Uri("http://localhost"),
                "/ModulePath",
                new KeyValuePair<string, string[]>[]
                {
                    new KeyValuePair<string, string[]>("SAMLResponse", new string[] { encoded })
                },
                Enumerable.Empty<KeyValuePair<string, string>>(),
                null);

            Action a = () => new AcsCommand().Run(r, Options.FromConfiguration);

            a.ShouldThrow<BadFormatSamlResponseException>()
                .WithMessage("The SAML response contains incorrect XML")
                .WithInnerException<XmlException>()
                .Where(ex => ex.Data["Saml2Response"] as string == "<foo />");
        }

        [TestMethod]
        public void AcsCommand_Run_ResponseIncludedInException()
        {
            string payload =
                "<saml2p:Response xmlns:saml2p=\"urn:oasis:names:tc:SAML:2.0:protocol\" "
                + "xmlns:saml2=\"urn:oasis:names:tc:SAML:2.0:assertion\" ID=\""
                + MethodBase.GetCurrentMethod().Name + "\" Version=\"2.0\" />";
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
            var r = new HttpRequestData(
                "POST",
                new Uri("http://localhost"),
                "/ModulePath",
                new KeyValuePair<string, string[]>[]
                {
                    new KeyValuePair<string, string[]>("SAMLResponse", new string[] { encoded })
                },
                Enumerable.Empty<KeyValuePair<string, string>>(),
                null);

            Action a = () => new AcsCommand().Run(r, Options.FromConfiguration);

            a.ShouldThrow<Exception>()
                .And.Data["Saml2Response"].Should().Be(payload);
        }

        [TestMethod]
        public void AcsCommand_Run_HandlesXmlExceptionWhenUnbindResultIsStillNull()
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("Not Xml"));

            var r = new HttpRequestData(
                "POST",
                new Uri("http://localhost"),
                "/ModulePath",
                new KeyValuePair<string, string[]>[]
                {
                    new KeyValuePair<string, string[]>("SAMLResponse", new string[] { encoded })
                },
                Enumerable.Empty<KeyValuePair<string, string>>(),
                null);

            Action a = () => new AcsCommand().Run(r, Options.FromConfiguration);

            a.ShouldThrow<BadFormatSamlResponseException>();
        }

        [TestMethod]
        public void AcsCommand_Run_HandlesExceptionWhenUnbindResultIsStillNull()
        {
            var issuer = new EntityId("http://bad.idp.example.com");
            var artifact = Saml2ArtifactBinding.CreateArtifact(issuer, 0);

            // Just spoil it to force an exception.
            artifact[3] = 5;

            var artifactString = Convert.ToBase64String(artifact);

            var r = new HttpRequestData(
                "POST",
                new Uri("http://localhost"),
                "/ModulePath",
                new KeyValuePair<string, string[]>[]
                {
                    new KeyValuePair<string, string[]>("SAMLart", new string[] { artifactString })
                },
                Enumerable.Empty<KeyValuePair<string, string>>(),
                null);

            Action a = () => new AcsCommand().Run(r, Options.FromConfiguration);

            // The real exception was masked by a NullRef in the exception
            // handler in AcsCommand.Run
            a.ShouldThrow<InvalidOperationException>();
        }

        [TestMethod]
        public void AcsCommand_Run_SuccessfulResult()
        {
            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
                xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>
                    https://idp.example.com
                </saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                <saml2:Assertion
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion1""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>
            </saml2p:Response>";

            var formValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                SignedXmlHelper.SignXml(response)));

            var r = new HttpRequestData(
                "POST",
                new Uri("http://localhost"),
                "/ModulePath",
                new KeyValuePair<string, string[]>[]
                {
                    new KeyValuePair<string, string[]>("SAMLResponse", new string[] { formValue })
                },
                Enumerable.Empty<KeyValuePair<string, string>>(),
                null);

            var ids = new ClaimsIdentity[] { new ClaimsIdentity("Federation"), new ClaimsIdentity("ClaimsAuthenticationManager") };
            ids[0].AddClaim(new Claim(ClaimTypes.NameIdentifier, "SomeUser", null, "https://idp.example.com"));
            ids[1].AddClaim(new Claim(ClaimTypes.Role, "RoleFromClaimsAuthManager", null, "ClaimsAuthenticationManagerStub"));

            var expected = new CommandResult()
            {
                Principal = new ClaimsPrincipal(ids),
                HttpStatusCode = HttpStatusCode.SeeOther,
                Location = new Uri("https://localhost/returnUrl"),
            };

            var options = StubFactory.CreateOptions();

            new AcsCommand().Run(r, options)
                .ShouldBeEquivalentTo(expected, opt => opt.IgnoringCyclicReferences());
        }

        [TestMethod]
        public void AcsCommand_Run_WithReturnUrl_SuccessfulResult()
        {
            var idp = Options.FromConfiguration.IdentityProviders.Default;

            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
                xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                ID = """ + MethodBase.GetCurrentMethod().Name + @""" InResponseTo = ""InResponseToId"" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>
                    https://idp.example.com
                </saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                <saml2:Assertion
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion2""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>
            </saml2p:Response>";

            var responseFormValue = Convert.ToBase64String
                (Encoding.UTF8.GetBytes(SignedXmlHelper.SignXml(response)));
            var relayStateFormValue = "rs1234";

            var r = new HttpRequestData(
                "POST",
                new Uri("http://localhost"),
                "/ModulePath",
                new KeyValuePair<string, string[]>[]
                {
                    new KeyValuePair<string, string[]>("SAMLResponse", new string[] { responseFormValue }),
                    new KeyValuePair<string, string[]>("RelayState", new string[] { relayStateFormValue })
                },
                new StoredRequestState(
                    new EntityId("https://idp.example.com"),
                    new Uri("http://localhost/testUrl.aspx"),
                    new Saml2Id("InResponseToId"),
                    null)
                );

            var ids = new ClaimsIdentity[] { new ClaimsIdentity("Federation"), new ClaimsIdentity("ClaimsAuthenticationManager") };
            ids[0].AddClaim(new Claim(ClaimTypes.NameIdentifier, "SomeUser", null, "https://idp.example.com"));
            ids[1].AddClaim(new Claim(ClaimTypes.Role, "RoleFromClaimsAuthManager", null, "ClaimsAuthenticationManagerStub"));

            var expected = new CommandResult()
            {
                Principal = new ClaimsPrincipal(ids),
                HttpStatusCode = HttpStatusCode.SeeOther,
                Location = new Uri("http://localhost/testUrl.aspx"),
                ClearCookieName = "Kentor." + relayStateFormValue
            };

            new AcsCommand().Run(r, StubFactory.CreateOptions())
                .ShouldBeEquivalentTo(expected, opt => opt.IgnoringCyclicReferences());
        }

        [TestMethod]
        public void AcsCommand_Run_WithReturnUrl_SuccessfulResult_NoConfigReturnUrl()
        {
            var idp = Options.FromConfiguration.IdentityProviders.Default;

            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
                xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                ID = """ + MethodBase.GetCurrentMethod().Name + @""" InResponseTo = ""InResponseToId"" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>
                    https://idp.example.com
                </saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                <saml2:Assertion
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion2""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>
            </saml2p:Response>";

            var responseFormValue = Convert.ToBase64String
                (Encoding.UTF8.GetBytes(SignedXmlHelper.SignXml(response)));
            var relayStateFormValue = "rs1234";

            var r = new HttpRequestData(
                "POST",
                new Uri("http://localhost"),
                "/ModulePath",
                new KeyValuePair<string, string[]>[]
                {
                    new KeyValuePair<string, string[]>("SAMLResponse", new string[] { responseFormValue }),
                    new KeyValuePair<string, string[]>("RelayState", new string[] { relayStateFormValue })
                },
                new StoredRequestState(
                    new EntityId("https://idp.example.com"),
                    new Uri("http://localhost/testUrl.aspx"),
                    new Saml2Id("InResponseToId"),
                    null)
                );

            var options = StubFactory.CreateOptions();
            options.SPOptions.ReturnUrl = null;

            new AcsCommand().Invoking(c => c.Run(r, options))
                .ShouldNotThrow();
        }

        [TestMethod]
        public void AcsCommand_Run_UnsolicitedResponse_ThrowsOnNoConfiguredReturnUrl()
        {
           var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
                xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>
                    https://idp.example.com
                </saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                <saml2:Assertion
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>
            </saml2p:Response>";

            var formValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                SignedXmlHelper.SignXml(response)));

            var r = new HttpRequestData(
                "POST",
                new Uri("http://localhost"),
                "/ModulePath",
                new KeyValuePair<string, string[]>[]
                {
                    new KeyValuePair<string, string[]>("SAMLResponse", new string[] { formValue })
                },
                Enumerable.Empty<KeyValuePair<string, string>>(),
                null);

            var options = StubFactory.CreateOptions();
            options.SPOptions.ReturnUrl = null;

            new AcsCommand().Invoking(a => a.Run(r, options))
                .ShouldThrow<ConfigurationErrorsException>().WithMessage(AcsCommand.UnsolicitedMissingReturnUrlMessage);
        }

        [TestMethod]
        public void AcsCommand_Run_Response_ThrowsOnNoStoredNorConfiguredReturnUrl()
        {
            var response =
             @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
                xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z""
                InResponseTo=""InResponseToId"">
                <saml2:Issuer>
                    https://idp.example.com
                </saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                <saml2:Assertion
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>
            </saml2p:Response>";

            var formValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                SignedXmlHelper.SignXml(response)));

            var r = new HttpRequestData(
                "POST",
                new Uri("http://localhost"),
                "/ModulePath",
                new KeyValuePair<string, string[]>[]
                {
                    new KeyValuePair<string, string[]>("SAMLResponse", new string[] { formValue })
                },
                new StoredRequestState(new EntityId("https://idp.example.com"), null, new Saml2Id("InResponseToId"), null));

            var options = StubFactory.CreateOptions();
            options.SPOptions.ReturnUrl = null;

            new AcsCommand().Invoking(a => a.Run(r, options))
                .ShouldThrow<ConfigurationErrorsException>().WithMessage(AcsCommand.SpInitiatedMissingReturnUrl);
        }

        [TestMethod]
        public void AcsCommand_Run_UsesBindingFromNotification()
        {
            var options = StubFactory.CreateOptions();
            options.Notifications.GetBinding = r => new StubSaml2Binding();

            var subject = new AcsCommand();
            subject.Invoking(s => s.Run(new HttpRequestData("GET", new Uri("http://host")), options))
                .ShouldThrow<NotImplementedException>()
                .WithMessage("StubSaml2Binding.*");
        }

        [TestMethod]
        public void AcsCommand_Run_CallsNotifications()
        {
            var messageId = MethodBase.GetCurrentMethod().Name;
            var response =
             @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
                xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                ID = """ + messageId + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>
                    https://idp.example.com
                </saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                <saml2:Assertion
                Version=""2.0"" ID=""" + messageId + @"_Assertion""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>
            </saml2p:Response>";

            var formValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                SignedXmlHelper.SignXml(response)));

            var requestData = new HttpRequestData(
                "POST",
                new Uri("http://localhost"),
                "/ModulePath",
                new KeyValuePair<string, string[]>[]
                {
                    new KeyValuePair<string, string[]>("SAMLResponse", new string[] { formValue })
                },
                null);

            var options = StubFactory.CreateOptions();

            var responseUnboundCalled = false;
            options.Notifications.MessageUnbound = ur =>
            {
                ur.Should().NotBeNull();
                responseUnboundCalled = true;
            };

            CommandResult notifiedCommandResult = null;
            options.Notifications.AcsCommandResultCreated = (cr, r) =>
            {
                notifiedCommandResult = cr;
                r.Id.Value.Should().Be(messageId);
            };

            new AcsCommand().Run(requestData, options)
                .Should().BeSameAs(notifiedCommandResult);

            responseUnboundCalled.Should().BeTrue("the ResponseUnbound notification should have been called.");
        }

        [TestMethod]
        public void AcsCommand_Run_ExtractsSessionNotOnOrAfter()
        {
            var messageId = MethodBase.GetCurrentMethod().Name;
            var response =
             $@"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
                xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                ID = ""{messageId}"" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>
                    https://idp.example.com
                </saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                <saml2:Assertion
                Version=""2.0"" ID=""{messageId}_Assertion""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                    <saml2:AuthnStatement AuthnInstant=""{DateTime.UtcNow.ToSaml2DateTimeString()}"" SessionNotOnOrAfter = ""2200-01-01T00:00:00Z"">
                        <saml2:AuthnContext>
                            <saml2:AuthnContextClassRef>urn:oasis:names:tc:SAML:2.0:ac:classes:PasswordProtectedTransport</saml2:AuthnContextClassRef>
                        </saml2:AuthnContext>
                    </saml2:AuthnStatement>
                </saml2:Assertion>
            </saml2p:Response>";

            var formValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                SignedXmlHelper.SignXml(response)));

            var requestData = new HttpRequestData(
                "POST",
                new Uri("http://localhost"),
                "/ModulePath",
                new KeyValuePair<string, string[]>[]
                {
                    new KeyValuePair<string, string[]>("SAMLResponse", new string[] { formValue })
                },
                null);

            var options = StubFactory.CreateOptions();

            var subject = new AcsCommand();
            var actual = subject.Run(requestData, options);

            actual.SessionNotOnOrAfter.Should().Be(new DateTime(2200, 01, 01, 0, 0, 0, DateTimeKind.Utc));
        }

        [TestMethod]
        public void AcsCommand_Run_SessionNotOnOrAfterNullIfNotSpecifiedInResponse()
        {
            var messageId = MethodBase.GetCurrentMethod().Name;
            var response =
             $@"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
                xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                ID = ""{messageId}"" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>
                    https://idp.example.com
                </saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                <saml2:Assertion
                Version=""2.0"" ID=""{messageId}_Assertion""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                    <saml2:AuthnStatement AuthnInstant=""{DateTime.UtcNow.ToSaml2DateTimeString()}"">
                        <saml2:AuthnContext>
                            <saml2:AuthnContextClassRef>urn:oasis:names:tc:SAML:2.0:ac:classes:PasswordProtectedTransport</saml2:AuthnContextClassRef>
                        </saml2:AuthnContext>
                    </saml2:AuthnStatement>
                </saml2:Assertion>
            </saml2p:Response>";

            var formValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                SignedXmlHelper.SignXml(response)));

            var requestData = new HttpRequestData(
                "POST",
                new Uri("http://localhost"),
                "/ModulePath",
                new KeyValuePair<string, string[]>[]
                {
                    new KeyValuePair<string, string[]>("SAMLResponse", new string[] { formValue })
                },
                null);

            var options = StubFactory.CreateOptions();

            var subject = new AcsCommand();
            var actual = subject.Run(requestData, options);

            actual.SessionNotOnOrAfter.Should().NotHaveValue();
        }
    }
}