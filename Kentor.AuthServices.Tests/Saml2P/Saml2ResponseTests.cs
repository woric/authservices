﻿using FluentAssertions;
using Kentor.AuthServices.Configuration;
using Kentor.AuthServices.Tests.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IdentityModel.Metadata;
using System.IdentityModel.Tokens;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.IO;
using Kentor.AuthServices.Saml2P;
using System.Reflection;
using System.IdentityModel.Selectors;
using Kentor.AuthServices.Exceptions;

namespace Kentor.AuthServices.Tests.Saml2P
{
    [TestClass]
    public class Saml2ResponseTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            SignedXmlHelper.RemoveGlobalSha256XmlSignatureSupport();
        }

        [TestMethod]
        public void Saml2Response_Read_BasicParams()
        {
            string response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
                <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z""
            InResponseTo = ""InResponseToId""
            Destination=""http://destination.example.com"">
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Requester"" />
                    <saml2p:StatusMessage>Unable to encrypt assertion</saml2p:StatusMessage>
                </saml2p:Status>
            </saml2p:Response>";

            var expected = new
            {
                Id = new Saml2Id(MethodBase.GetCurrentMethod().Name),
                IssueInstant = new DateTime(2013, 01, 01, 0, 0, 0, DateTimeKind.Utc),
                Status = Saml2StatusCode.Requester,
                StatusMessage = "Unable to encrypt assertion",
                Issuer = new EntityId(null),
                DestinationUrl = new Uri("http://destination.example.com"),
                MessageName = "SAMLResponse",
                InResponseTo = new Saml2Id("InResponseToId"),
                RequestState = (StoredRequestState)null,
                SecondLevelStatus = (string)null,
                RelayState = (string)null,
            };

            Saml2Response.Read(response, expected.InResponseTo).ShouldBeEquivalentTo(
                expected, opt => opt
                    .Excluding(s => s.XmlElement)
                    .Excluding(s => s.SigningCertificate)
                    .Excluding(s => s.SessionNotOnOrAfter));
        }

        [TestMethod]
        public void Saml2Response_Read_ThrowsOnNonXml()
        {
            Action a = () => Saml2Response.Read("not xml");

            a.ShouldThrow<XmlException>();
        }

        [TestMethod]
        public void Saml2Response_Read_ThrowsWrongRootNodeName()
        {
            Action a = () => Saml2Response.Read("<saml2p:NotResponse xmlns:saml2p=\"urn:oasis:names:tc:SAML:2.0:protocol\" />");

            a.ShouldThrow<XmlException>()
                .WithMessage("Expected a SAML2 assertion document");
        }

        [TestMethod]
        public void Saml2Response_Read_ThrowsWrongRootNamespace()
        {
            Action a = () => Saml2Response.Read("<saml2p:Response xmlns:saml2p=\"something\" /> ");
            a.ShouldThrow<XmlException>()
                .WithMessage("Expected a SAML2 assertion document");
        }

        [TestMethod]
        public void Saml2Response_Read_ThrowsOnWrongVersion()
        {
            Action a = () => Saml2Response.Read("<saml2p:Response xmlns:saml2p=\""
                + Saml2Namespaces.Saml2P + "\" Version=\"wrong\" />");

            a.ShouldThrow<XmlException>()
                .WithMessage("Wrong or unsupported SAML2 version");

        }

        [TestMethod]
        public void Saml2Response_Read_ThrowsOnMalformedDestination()
        {
            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            Destination = ""not_a_uri""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
            <saml2:Issuer>
                https://some.issuer.example.com
            </saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Requester"" />
                </saml2p:Status>
            </saml2p:Response>";

            Action a = () => Saml2Response.Read(response);

            a.ShouldThrow<BadFormatSamlResponseException>()
                .WithMessage("Destination value was not a valid Uri");
        }

        [TestMethod]
        public void Saml2Response_Read_Issuer()
        {
            var response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
            <saml2:Issuer>
                https://some.issuer.example.com
            </saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Requester"" />
                </saml2p:Status>
            </saml2p:Response>";

            Saml2Response.Read(response).Issuer.Id.Should().Be("https://some.issuer.example.com");
        }

        [TestMethod]
        public void Saml2Response_GetClaims_ThrowOnMissingSignatureInResponseAndAnyAssertion()
        {
            var response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                <saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion1""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>
                <saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
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

            Action a = () => Saml2Response.Read(response).GetClaims(Options.FromConfiguration);

            a.ShouldThrow<Saml2ResponseFailedValidationException>()
                .WithMessage("The SAML Response is not signed and contains unsigned Assertions. Response cannot be trusted.");
        }

        [TestMethod]
        public void Saml2Response_GetClaims_CorrectSignedResponseMessage()
        {
            var response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                <saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
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

            var signedResponse = SignedXmlHelper.SignXml(response);

            Action a = () => Saml2Response.Read(signedResponse).GetClaims(Options.FromConfiguration);
            a.ShouldNotThrow();
        }

        [TestMethod]
        public void Saml2Response_GetClaims_CorrectSignedResponseMessageSecondaryKey()
        {
            var response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://twokeys.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                <saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion1""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://twokeys.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>
            </saml2p:Response>";

            var signedResponse = SignedXmlHelper.SignXml(response);

            var options = StubFactory.CreateOptions();

            var idp = new IdentityProvider(
                new EntityId("https://twokeys.example.com"), options.SPOptions)
            {
                AllowUnsolicitedAuthnResponse = true
            };

            idp.SigningKeys.AddConfiguredKey(SignedXmlHelper.TestKey2);
            idp.SigningKeys.AddConfiguredKey(SignedXmlHelper.TestKey);

            options.IdentityProviders.Add(idp);

            Action a = () => Saml2Response.Read(signedResponse).GetClaims(options);
            a.ShouldNotThrow();
        }

        [TestMethod]
        public void Saml2Response_GetClaims_CorrectSignedResponseMessage_WithAuthnStatementGeneratesLogoutNameIdentifierAllNameIdProperties()
        {
            var response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                <saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion1""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID
                            NameQualifier=""NameQualifier""
                            SPNameQualifier=""SPNameQualifier""
                            Format=""urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress""
                            SPProvidedID=""SPProvidedID""
                            >someone@example.com</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                    <saml2:AuthnStatement AuthnInstant=""2013-09-25T00:00:00Z"" SessionIndex=""17"" >
                        <saml2:AuthnContext>
                            <saml2:AuthnContextClassRef>urn:oasis:names:tc:SAML:2.0:ac:classes:PasswordProtectedTransport</saml2:AuthnContextClassRef>
                            <saml2:AuthnContextDeclRef>http://custom/password/form/consumer</saml2:AuthnContextDeclRef>
                        </saml2:AuthnContext>
                    </saml2:AuthnStatement>
                </saml2:Assertion>
            </saml2p:Response>";

            var signedResponse = SignedXmlHelper.SignXml(response);

            var result = Saml2Response.Read(signedResponse).GetClaims(Options.FromConfiguration);

            var logoutInfoClaim = result.Single().Claims.SingleOrDefault(c => c.Type == AuthServicesClaimTypes.LogoutNameIdentifier);
            logoutInfoClaim.Should().NotBeNull("the LogoutInfo claim should be generated");
            logoutInfoClaim.Value.Should().Be("NameQualifier,SPNameQualifier,urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress,SPProvidedID,someone@example.com");
        }

        [TestMethod]
        public void Saml2Response_GetClaims_CorrectSignedResponseMessage_WithAuthnStatementGeneratesLogoutInfo()
        {
            var response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                <saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion1""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeOne</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                    <saml2:AuthnStatement AuthnInstant=""2013-09-25T00:00:00Z"" SessionIndex=""17"" >
                        <saml2:AuthnContext>
                            <saml2:AuthnContextClassRef>urn:oasis:names:tc:SAML:2.0:ac:classes:PasswordProtectedTransport</saml2:AuthnContextClassRef>
                            <saml2:AuthnContextDeclRef>http://custom/password/form/consumer</saml2:AuthnContextDeclRef>
                        </saml2:AuthnContext>
                    </saml2:AuthnStatement>
                </saml2:Assertion>
            </saml2p:Response>";

            var signedResponse = SignedXmlHelper.SignXml(response);

            var result = Saml2Response.Read(signedResponse).GetClaims(Options.FromConfiguration);

            var logoutInfoClaim = result.Single().Claims.SingleOrDefault(c => c.Type == AuthServicesClaimTypes.LogoutNameIdentifier);
            logoutInfoClaim.Should().NotBeNull("the Logout name identifier claim should be generated");
            logoutInfoClaim.Value.Should().Be(",,,,SomeOne");

            var sessionIdClaim = result.Single().Claims.SingleOrDefault(c => c.Type == AuthServicesClaimTypes.SessionIndex);
            sessionIdClaim.Should().NotBeNull("the Session ID claim should be generated");
            sessionIdClaim.Value.Should().Be("17");
        }

        [TestMethod]
        public void Saml2Response_GetClaims_CorrectSignedSingleAssertionInResponseMessage()
        {
            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                {0}
            </saml2p:Response>";

            var assertion =
            @"<saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion1""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>";


            var signedAssertion = SignedXmlHelper.SignXml(assertion);
            var signedResponse = string.Format(response, signedAssertion);

            Action a = () => Saml2Response.Read(signedResponse).GetClaims(Options.FromConfiguration);
            a.ShouldNotThrow();
        }

        [TestMethod]
        public void Saml2Response_GetClaims_CorrectSignedSingleAssertion_WithKeyInfo_InResponseMessage()
        {
            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                {0}
            </saml2p:Response>";

            var assertion =
            @"<saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_assertion""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>";

            var signedAssertion = SignedXmlHelper.SignXml(assertion, true, false);
            var signedResponse = string.Format(response, signedAssertion);

            Action a = () => Saml2Response.Read(signedResponse).GetClaims(Options.FromConfiguration);
            a.ShouldNotThrow();
        }

        [TestMethod]
        public void Saml2Response_GetClaims_CorrectSignedMultipleAssertionInResponseMessage()
        {
            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                {0}
                {1}
            </saml2p:Response>";

            var assertion1 = @"<saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion1""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>";

            var assertion2 = @"<saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion2""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser2</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>";

            var signedAssertion1 = SignedXmlHelper.SignXml(assertion1);
            var signedAssertion2 = SignedXmlHelper.SignXml(assertion2);
            var signedResponse = string.Format(response, signedAssertion1, signedAssertion2);

            Action a = () => Saml2Response.Read(signedResponse).GetClaims(Options.FromConfiguration);
            a.ShouldNotThrow();
        }

        [TestMethod]
        public void Saml2Response_GetClaims_CorrectSignedMultipleAssertion_WithKeyInfo_InResponseMessage()
        {
            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                {0}
                {1}
            </saml2p:Response>";

            var assertion1 = @"<saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion1""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>";

            var assertion2 = @"<saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion2""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser2</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>";

            var signedAssertion1 = SignedXmlHelper.SignXml(assertion1, true, false);
            var signedAssertion2 = SignedXmlHelper.SignXml(assertion2, true, false);
            var signedResponse = string.Format(response, signedAssertion1, signedAssertion2);

            Action a = () => Saml2Response.Read(signedResponse).GetClaims(Options.FromConfiguration);
            a.ShouldNotThrow();
        }

        [TestMethod]
        public void Saml2Response_GetClaims_ThrowsOnMultipleAssertionInUnsignedResponseMessageButNotAllSigned()
        {
            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                {0}
                {1}
            </saml2p:Response>";

            var assertion1 = @"<saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion1""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>";

            var assertion2 = @"<saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion2""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser2</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>";


            var signedAssertion1 = SignedXmlHelper.SignXml(assertion1);
            var signedResponse = string.Format(response, signedAssertion1, assertion2);

            Action a = () => Saml2Response.Read(signedResponse).GetClaims(Options.FromConfiguration);

            a.ShouldThrow<Saml2ResponseFailedValidationException>()
                .WithMessage("The SAML Response is not signed and contains unsigned Assertions. Response cannot be trusted.");
        }

        [TestMethod]
        public void Saml2Response_GetClaims_ThrowsOnTamperedAssertionWithAssertionSignature()
        {
            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                {0}
                {1}
            </saml2p:Response>";

            var assertion1 = @"<saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion1""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>";

            var assertion2 = @"<saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion2""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser2</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>";

            var signedAssertion1 = SignedXmlHelper.SignXml(assertion1);
            var signedAssertion2 = SignedXmlHelper.SignXml(assertion2).Replace("SomeUser2", "SomeOtherUser");
            var signedResponse = string.Format(response, signedAssertion1, signedAssertion2);

            Action a = () => Saml2Response.Read(signedResponse).GetClaims(Options.FromConfiguration);

            a.ShouldThrow<InvalidSignatureException>()
                .WithMessage("Signature didn't verify. Have the contents been tampered with?");
        }

        [TestMethod]
        public void Saml2Response_GetClaims_ThrowsOnAssertionInjectionWithAssertionSignature()
        {
            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                {0}
                {1}
            </saml2p:Response>";

            var assertion1 = @"<saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion1""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>";

            var assertionToInject = @"<saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion2""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser2</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>";

            var signedAssertion1 = SignedXmlHelper.SignXml(assertion1);

            var signedAssertion1Doc = new XmlDocument { PreserveWhitespace = true };
            signedAssertion1Doc.LoadXml(signedAssertion1);

            var signatureToCopy = signedAssertion1Doc.DocumentElement["Signature", SignedXml.XmlDsigNamespaceUrl];

            var assertionToInjectDoc = new XmlDocument { PreserveWhitespace = true };
            assertionToInjectDoc.LoadXml(assertionToInject);

            assertionToInjectDoc.DocumentElement.AppendChild(assertionToInjectDoc.ImportNode(signatureToCopy, true));

            var signedAssertionToInject = assertionToInjectDoc.OuterXml;

            var signedResponse = string.Format(response, signedAssertion1, signedAssertionToInject);

            Action a = () => Saml2Response.Read(signedResponse).GetClaims(Options.FromConfiguration);

            a.ShouldThrow<InvalidSignatureException>()
                .WithMessage("Incorrect reference on Xml signature. The reference must be to the root element of the element containing the signature.");
        }

        [TestMethod]
        public void Saml2Response_GetClaims_ReturnsExistingResultOnSecondGetClaimsCall()
        {
            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
            </saml2p:Response>";

            var signedResponse = SignedXmlHelper.SignXml(response);

            var samlResponse = Saml2Response.Read(signedResponse);

            Action a = () => samlResponse.GetClaims(Options.FromConfiguration);

            a.ShouldNotThrow();
            a.ShouldNotThrow();
        }

        [TestMethod]
        public void Saml2Response_GetClaims_CorrectEncryptedSingleAssertion_SignedResponse()
        {
            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                {0}
            </saml2p:Response>";
                
            var assertion =
            @"<saml2:Assertion Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion1""
                IssueInstant=""2013-09-25T00:00:00Z"" xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>UserIDInsideEncryptedAssertion</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>";

            var encryptedAssertion = SignedXmlHelper.EncryptAssertion(assertion);
            var signedResponse = SignedXmlHelper.SignXml(string.Format(response, encryptedAssertion));

            var options = StubFactory.CreateOptions();
            options.SPOptions.ServiceCertificates.Add(new ServiceCertificate { Certificate = SignedXmlHelper.TestCert2 });

            var claims = Saml2Response.Read(signedResponse).GetClaims(options);
            claims.Count().Should().Be(1);
            claims.First().FindFirst(ClaimTypes.NameIdentifier).Value.Should().Be("UserIDInsideEncryptedAssertion");
        }

        [TestMethod]
        public void Saml2Response_GetClaims_CorrectEncryptedSingleAssertion_SignedAssertion()
        {
            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                {0}
            </saml2p:Response>";

            var assertion =
            @"<saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion1""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>";

            var signedAssertion = SignedXmlHelper.SignXml(assertion);
            var encryptedAssertion = SignedXmlHelper.EncryptAssertion(signedAssertion);
            var responseWithAssertion = string.Format(response, encryptedAssertion);

            var claims = Saml2Response.Read(responseWithAssertion).GetClaims(Options.FromConfiguration);
            claims.Count().Should().Be(1);
            claims.First().FindFirst(ClaimTypes.NameIdentifier).Value.Should().Be("SomeUser");
        }

        [TestMethod]
        public void Saml2Response_GetClaims_CorrectEncryptedSingleAssertion_AndMultipleCertsConfigured()
        {
            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                {0}
            </saml2p:Response>";

            var assertion =
            @"<saml2:Assertion Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion1""
                IssueInstant=""2013-09-25T00:00:00Z"" xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>UserIDInsideEncryptedAssertion</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>";

            var encryptedAssertion = SignedXmlHelper.EncryptAssertion(assertion);
            var signedResponse = SignedXmlHelper.SignXml(string.Format(response, encryptedAssertion));

            var options = StubFactory.CreateOptions();
            options.SPOptions.ServiceCertificates.Add(new ServiceCertificate { Certificate = SignedXmlHelper.TestCert });
            options.SPOptions.ServiceCertificates.Add(new ServiceCertificate { Certificate = SignedXmlHelper.TestCert2 });

            var claims = Saml2Response.Read(signedResponse).GetClaims(options);
            claims.Count().Should().Be(1);
            claims.First().FindFirst(ClaimTypes.NameIdentifier).Value.Should().Be("UserIDInsideEncryptedAssertion");
        }

        [TestMethod]
        public void Saml2Response_GetClaims_ThrowsWhenEncryptedAssertion_WrongCertsConfigured()
        {
            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                {0}
            </saml2p:Response>";

            var assertion =
            @"<saml2:Assertion Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion1""
                IssueInstant=""2013-09-25T00:00:00Z"" xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>UserIDInsideEncryptedAssertion</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>";

            var encryptedAssertion = SignedXmlHelper.EncryptAssertion(assertion, false, SignedXmlHelper.TestCert2);
            var signedResponse = SignedXmlHelper.SignXml(string.Format(response, encryptedAssertion));

            var options = StubFactory.CreateOptions();
            options.SPOptions.ServiceCertificates.Add(new ServiceCertificate { Certificate = SignedXmlHelper.TestCert });

            Action a = () => Saml2Response.Read(signedResponse).GetClaims(options);

            a.ShouldThrow<Saml2ResponseFailedValidationException>()
                .WithMessage("Encrypted Assertion(s) could not be decrypted using the configured Service Certificate(s).");
        }

        [TestMethod]
        public void Saml2Response_GetClaims_CorrectEncryptedSingleAssertion_OAEP()
        {
            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                {0}
            </saml2p:Response>";

            var assertion =
            @"<saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion1""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>";

            var signedAssertion = SignedXmlHelper.SignXml(assertion);
            var encryptedAssertion = SignedXmlHelper.EncryptAssertion(signedAssertion, useOaep: true);
            var responseWithAssertion = string.Format(response, encryptedAssertion);

            var claims = Saml2Response.Read(responseWithAssertion).GetClaims(Options.FromConfiguration);
            claims.Count().Should().Be(1);
            claims.First().FindFirst(ClaimTypes.NameIdentifier).Value.Should().Be("SomeUser");
        }

        [TestMethod]
        public void Saml2Response_GetClaims_CorrectEncryptedSingleAssertion_UsingWIF()
        {
            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                {0}
            </saml2p:Response>";

            var assertion = new Saml2Assertion(new Saml2NameIdentifier("https://idp.example.com"));
            assertion.Subject = new Saml2Subject(new Saml2NameIdentifier("WIFUser"));
            assertion.Subject.SubjectConfirmations.Add(new Saml2SubjectConfirmation(new Uri("urn:oasis:names:tc:SAML:2.0:cm:bearer")));
            assertion.Conditions = new Saml2Conditions { NotOnOrAfter = new DateTime(2100, 1, 1) };

            var token = new Saml2SecurityToken(assertion);
            var handler = new Saml2SecurityTokenHandler();

            assertion.SigningCredentials = new X509SigningCredentials(SignedXmlHelper.TestCert,
                signatureAlgorithm: SecurityAlgorithms.RsaSha1Signature, 
                digestAlgorithm: SecurityAlgorithms.Sha1Digest);

            assertion.EncryptingCredentials = new EncryptedKeyEncryptingCredentials(
                SignedXmlHelper.TestCert2,
                keyWrappingAlgorithm: SecurityAlgorithms.RsaOaepKeyWrap,
                keySizeInBits: 256,
                encryptionAlgorithm: SecurityAlgorithms.Aes192Encryption);

            string assertionXml = String.Empty;
            using (var sw = new StringWriter())
            {
                using (var xw = XmlWriter.Create(sw, new XmlWriterSettings { OmitXmlDeclaration = true }))
                {
                    handler.WriteToken(xw, token);
                }
                assertionXml = sw.ToString();
            }
            var responseWithAssertion = string.Format(response, assertionXml);

            var claims = Saml2Response.Read(responseWithAssertion).GetClaims(Options.FromConfiguration);
            claims.Count().Should().Be(1);
            claims.First().FindFirst(ClaimTypes.NameIdentifier).Value.Should().Be("WIFUser");
        }

        [TestMethod]
        public void Saml2Response_GetClaims_ThrowsOnEncryptedAssertionWithoutSignature()
        {
            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                {0}
            </saml2p:Response>";

            var assertion =
            @"<saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion1""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>";

            var encryptedAssertion = SignedXmlHelper.EncryptAssertion(assertion);
            var responseWithAssertion = string.Format(response, encryptedAssertion);

            Action a = () => Saml2Response.Read(responseWithAssertion).GetClaims(Options.FromConfiguration);

            a.ShouldThrow<Saml2ResponseFailedValidationException>()
                .WithMessage("The SAML Response is not signed and contains unsigned Assertions. Response cannot be trusted.");
        }

        [TestMethod]
        public void Saml2Response_GetClaims_ThrowsOnTamperedSignatureInEncryptedAssertion()
        {
            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                {0}
            </saml2p:Response>";

            var assertion =
            @"<saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion1""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>";

            var signedAssertion = SignedXmlHelper.SignXml(assertion);
            var tamperedAssertion = signedAssertion.Replace("SomeUser", "AnotherUser");
            var encryptedAssertion = SignedXmlHelper.EncryptAssertion(tamperedAssertion);
            var responseWithAssertion = string.Format(response, encryptedAssertion);

            Action a = () => Saml2Response.Read(responseWithAssertion).GetClaims(Options.FromConfiguration);

            a.ShouldThrow<InvalidSignatureException>()
                .WithMessage("Signature didn't verify. Have the contents been tampered with?");
        }

        [TestMethod]
        public void Saml2Response_GetClaims_ThrowsOnEncryptedAssertionAndNoServiceCert()
        {
            var response =
            @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                {0}
            </saml2p:Response>";

            var assertion =
            @"<saml2:Assertion Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion1""
                IssueInstant=""2013-09-25T00:00:00Z"" xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>UserIDInsideEncryptedAssertion</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>";

            var encryptedAssertion = SignedXmlHelper.EncryptAssertion(assertion);
            var signedResponse = SignedXmlHelper.SignXml(string.Format(response, encryptedAssertion));

            var options = StubFactory.CreateOptions();

            Action a = () => Saml2Response.Read(signedResponse).GetClaims(options);
            a.ShouldThrow<Saml2ResponseFailedValidationException>();
        }

        [TestMethod]
        public void Saml2Response_GetClaims_CreateIdentities()
        {
            var response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                <saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"1""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>
                <saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"2""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeOtherUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                </saml2:Assertion>
            </saml2p:Response>";

            var c1 = new ClaimsIdentity("Federation");
            c1.AddClaim(new Claim(ClaimTypes.NameIdentifier, "SomeUser", null, "https://idp.example.com"));
            var c2 = new ClaimsIdentity("Federation");
            c2.AddClaim(new Claim(ClaimTypes.NameIdentifier, "SomeOtherUser", null, "https://idp.example.com"));

            var expected = new ClaimsIdentity[] { c1, c2 };

            var r = Saml2Response.Read(SignedXmlHelper.SignXml(response));

            r.GetClaims(StubFactory.CreateOptions())
                .ShouldBeEquivalentTo(expected, opt => opt.IgnoringCyclicReferences());
        }

        [TestMethod]
        public void Saml2Response_GetClaims_SavesBootstrapContext()
        {
            var assertion =
            @"<saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + @"_Assertion""
                IssueInstant=""2013-09-25T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2:Subject>
                    <saml2:NameID>SomeUser</saml2:NameID>
                    <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                </saml2:Subject>
                <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
            </saml2:Assertion>";

            var response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>"
            + assertion +
            "</saml2p:Response>";

            var options = StubFactory.CreateOptions();

            options.SPOptions.Saml2PSecurityTokenHandler.Configuration.SaveBootstrapContext = true;

            var expected = options.SPOptions.Saml2PSecurityTokenHandler.ReadToken(XmlReader.Create(new StringReader(assertion)));

            var r = Saml2Response.Read(SignedXmlHelper.SignXml(response));

            var subject = r.GetClaims(options).Single().BootstrapContext;

            subject.As<BootstrapContext>().SecurityToken.ShouldBeEquivalentTo(expected);
        }

        [TestMethod]
        public void Saml2Response_GetClaims_ThrowsOnWrongAudience()
        {
            var response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
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
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" >
                        <saml2:AudienceRestriction>
                            <saml2:Audience>https://example.com/wrong/audience</saml2:Audience>
                        </saml2:AudienceRestriction>
                    </saml2:Conditions>
                </saml2:Assertion>
            </saml2p:Response>";

            response = SignedXmlHelper.SignXml(response);

            var subject = Saml2Response.Read(response);

            var options = StubFactory.CreateOptions();
            options.SPOptions.SystemIdentityModelIdentityConfiguration.AudienceRestriction.AudienceMode
                = AudienceUriMode.Always;

            subject.Invoking(s => s.GetClaims(options))
                .ShouldThrow<AudienceUriValidationFailedException>();
        }

        [TestMethod]
        public void Saml2Response_GetClaims_IgnoresAudienceIfConfiguredWithNever()
        {
            var response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
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
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" >
                        <saml2:AudienceRestriction>
                            <saml2:Audience>https://example.com/wrong/audience</saml2:Audience>
                        </saml2:AudienceRestriction>
                    </saml2:Conditions>
                </saml2:Assertion>
            </saml2p:Response>";

            response = SignedXmlHelper.SignXml(response);

            var subject = Saml2Response.Read(response);

            var options = StubFactory.CreateOptions();
            options.SPOptions.SystemIdentityModelIdentityConfiguration
                .AudienceRestriction.AudienceMode = AudienceUriMode.Never;

            subject.Invoking(s => s.GetClaims(options)).ShouldNotThrow();
        }

        [TestMethod]
        public void Saml2Response_GetClaims_ThrowsOnExpired()
        {
            var response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
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
                    <saml2:Conditions NotOnOrAfter=""2013-06-30T08:00:00Z"" />
                </saml2:Assertion>
            </saml2p:Response>";

            response = SignedXmlHelper.SignXml(response);
            var r = Saml2Response.Read(response);

            Action a = () => r.GetClaims(Options.FromConfiguration);

            a.ShouldThrow<SecurityTokenExpiredException>();
        }

        [TestMethod]
        public void Saml2Response_GetClaims_CorrectInResponseTo()
        {
            var idp = Options.FromConfiguration.IdentityProviders.Default;

            var responseXML =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z""
            InResponseTo = ""abc123"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
            </saml2p:Response>";

            responseXML = SignedXmlHelper.SignXml(responseXML);

            var response = Saml2Response.Read(responseXML, new Saml2Id("abc123"));

            Action a = () => response.GetClaims(Options.FromConfiguration);
            a.ShouldNotThrow();
        }

        [TestMethod]
        public void Saml2Response_GetClaims_FalseOnMissingInResponseTo_IfDisallowed()
        {
            var responseXML =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp2.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Requester"" />
                </saml2p:Status>
            </saml2p:Response>";

            responseXML = SignedXmlHelper.SignXml(responseXML);

            var response = Saml2Response.Read(responseXML);

            Action a = () => response.GetClaims(Options.FromConfiguration);

            a.ShouldThrow<Saml2ResponseFailedValidationException>()
                .WithMessage("Unsolicited responses are not allowed for idp \"https://idp2.example.com\".");
        }

        [TestMethod]
        public void Saml2Response_GetClaims_MissingInResponseTo_IfAllowed()
        {
            var idp = Options.FromConfiguration.IdentityProviders.Default;

            var request = idp.CreateAuthenticateRequest(StubFactory.CreateAuthServicesUrls());

            var responseXML =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
            </saml2p:Response>";

            responseXML = SignedXmlHelper.SignXml(responseXML);

            var response = Saml2Response.Read(responseXML);

            Action a = () => response.GetClaims(Options.FromConfiguration);
            a.ShouldNotThrow();
        }

        [TestMethod]
        public void Saml2Response_Read_ThrowsOnIncorrectInResponseTo()
        {
            var idp = Options.FromConfiguration.IdentityProviders.Default;

            var responseXML =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z""
            InResponseTo = ""anothervalue"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Requester"" />
                </saml2p:Status>
            </saml2p:Response>";

            responseXML = SignedXmlHelper.SignXml(responseXML);

            Action a = () => Saml2Response.Read(responseXML, new Saml2Id("somevalue"));

            a.ShouldThrow<Saml2ResponseFailedValidationException>()
                .WithMessage("InResponseTo Id \"anothervalue\" in received response does not match Id \"somevalue\" of the sent request.");
        }

        [TestMethod]
        public void Saml2Response_Read_ThrowsOnInResponseTo_When_NoneExpected()
        {
            var idp = Options.FromConfiguration.IdentityProviders.Default;

            var responseXML =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z""
            InResponseTo = ""InResponseTo"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Requester"" />
                </saml2p:Status>
            </saml2p:Response>";

            responseXML = SignedXmlHelper.SignXml(responseXML);

            Action a = () => Saml2Response.Read(responseXML, null);

            a.ShouldThrow<UnexpectedInResponseToException>()
                .WithMessage("Received message contains unexpected InResponseTo \"InResponseTo\"*");
        }

        [TestMethod]
        public void Saml2Response_Read_ThrowsOnNoInResponseTo_When_OneWasExpected()
        {
            var idp = Options.FromConfiguration.IdentityProviders.Default;

            var responseXML =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Requester"" />
                </saml2p:Status>
            </saml2p:Response>";

            responseXML = SignedXmlHelper.SignXml(responseXML);

            Action a = () => Saml2Response.Read(responseXML, new Saml2Id("ExpectedId"));

            a.ShouldThrow<Saml2ResponseFailedValidationException>()
                .WithMessage("Expected message to contain InResponseTo \"ExpectedId\", but found none.");
        }

        [TestMethod]
        public void Saml2Response_GetClaims_ThrowsOnTamperedMessage()
        {
            var idp = Options.FromConfiguration.IdentityProviders.Default;

            var responseXML =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Requester"" />
                </saml2p:Status>
            </saml2p:Response>";

            responseXML = SignedXmlHelper.SignXml(responseXML);
            responseXML = responseXML.Replace("2013-01-01", "2015-01-01"); // Break signature.

            var response = Saml2Response.Read(responseXML, null);

            Action a = () =>
            {
                response.GetClaims(Options.FromConfiguration);
            };

            a.ShouldThrow<InvalidSignatureException>()
                .WithMessage("Signature didn't verify. Have the contents been tampered with?");

            // With an incorrect signature, a signature validation should be
            // thrown - even if we response is validate twice. In case
            // GetClaims/Validate doesn't cache the result it will instead
            // report a replay exception the second time because the replay
            // detection is done before the signature validation.

            a.ShouldThrow<InvalidSignatureException>()
                .WithMessage("Signature didn't verify. Have the contents been tampered with?");
        }

        [TestMethod]
        public void Saml2Response_GetClaims_ThrowsOnReplayAssertionId()
        {
            var response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
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

            response = SignedXmlHelper.SignXml(response);
            var r1 = Saml2Response.Read(response);
            r1.GetClaims(Options.FromConfiguration);

            var r2 = Saml2Response.Read(response);

            Action a = () => r2.GetClaims(Options.FromConfiguration);

            a.ShouldThrow<SecurityTokenReplayDetectedException>();
        }

        [TestMethod]
        public void Saml2Response_GetClaims_ThrowsOnReplayAssertionIdSameConfig()
        {
            var response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
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

            response = SignedXmlHelper.SignXml(response);
            var r1 = Saml2Response.Read(response);
            var options = StubFactory.CreateOptions();
            r1.GetClaims(options);

            var r2 = Saml2Response.Read(response);

            Action a = () => r2.GetClaims(options);

            a.ShouldThrow<SecurityTokenReplayDetectedException>();
        }

        [TestMethod]
        public void Saml2Response_GetClaims_NotThrowsOnReplayAssertionIdDifferentConfig()
        {
            var response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
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

            response = SignedXmlHelper.SignXml(response);
            var r1 = Saml2Response.Read(response);
            var options1 = StubFactory.CreateOptions();
            r1.GetClaims(options1);

            var r2 = Saml2Response.Read(response);

            var options2 = StubFactory.CreateOptions();
            Action a = () => r2.GetClaims(options2);

            a.ShouldNotThrow();
        }

        [TestMethod]
        public void Saml2Response_GetClaims_ThrowsOnEmptyOptions()
        {
            var response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Requester"" />
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

            var xml = SignedXmlHelper.SignXml(response);

            var subject = Saml2Response.Read(xml);

            Action a = () => subject.GetClaims(null);
            a.ShouldThrow<ArgumentNullException>().And.ParamName.Should().Be("options");
        }

        [TestMethod]
        public void Saml2Response_GetClaims_ThrowsOnStatusFailure()
        {
            var response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Requester"" />
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

            var xml = SignedXmlHelper.SignXml(response);

            var subject = Saml2Response.Read(xml);

            Action a = () => subject.GetClaims(Options.FromConfiguration);

            a.ShouldThrow<UnsuccessfulSamlOperationException>()
                .WithMessage("The Saml2Response must have status success to extract claims. Status: Requester.")
                .Where(x => x.Status == Saml2StatusCode.Requester);

        }

        [TestMethod]
        public void Saml2Response_GetClaims_ThrowsOnStatusFailure_IncludingSecondLevelMessage()
        {
            var response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusMessage>A status message</saml2p:StatusMessage>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Responder"">
                        <saml2p:StatusCode Value = ""urn:oasis:names:tc:SAML:2.0:status:RequestDenied"" />
                    </saml2p:StatusCode >
                </saml2p:Status >
            </saml2p:Response>";

            var xml = SignedXmlHelper.SignXml(response);

            var subject = Saml2Response.Read(xml);

            Action a = () => subject.GetClaims(Options.FromConfiguration);

            a.ShouldThrow<UnsuccessfulSamlOperationException>()
                .WithMessage("The Saml2Response must have status success to extract claims. Status: Responder. Message: A status message.")
                .Where(x => x.Status == Saml2StatusCode.Responder && x.StatusMessage == "A status message" && x.SecondLevelStatus == "urn:oasis:names:tc:SAML:2.0:status:RequestDenied");

        }

        [TestMethod]
        public void Saml2Response_DisplayStatusMessageInExceptionText()
        {
            var response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Requester"" />
                    <saml2p:StatusMessage>A status message</saml2p:StatusMessage>
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

            var xml = SignedXmlHelper.SignXml(response);

            var subject = Saml2Response.Read(xml);

            Action a = () => subject.GetClaims(Options.FromConfiguration);

            a.ShouldThrow<UnsuccessfulSamlOperationException>()
                .WithMessage("The Saml2Response must have status success to extract claims. Status: Requester. Message: A status message.")
                .Where(x => x.Status == Saml2StatusCode.Requester);

        }

        [TestMethod]
        public void Saml2Response_Ctor_FromData()
        {
            var issuer = new EntityId("http://idp.example.com");
            var identity = new ClaimsIdentity(new Claim[] 
            {
                new Claim(ClaimTypes.NameIdentifier, "JohnDoe") 
            });
            var response = new Saml2Response(issuer, null, null, null, identity);

            response.Issuer.Should().Be(issuer);
            response.GetClaims(Options.FromConfiguration)
                .Single()
                .ShouldBeEquivalentTo(identity);
        }

        [TestMethod]
        public void Saml2Response_Ctor_Nullcheck()
        {
            Action a = () => new Saml2Response(null, new Saml2Id("foo"));

            a.ShouldThrow<ArgumentNullException>()
                .And.ParamName.Should().Be("xml");
        }

        [TestMethod]
        public void Saml2Response_Xml_FromData_ContainsBasicData()
        {
            var issuer = new EntityId("http://idp.example.com");
            var nameId = "JohnDoe";
            var destination = "http://destination.example.com/";

            var identity = new ClaimsIdentity(new Claim[] 
            {
                new Claim(ClaimTypes.NameIdentifier, nameId) 
            });

            // Grab current time both before and after generating the response
            // to avoid heisenbugs if the second counter is updated while creating
            // the response.
            string before = DateTime.UtcNow.ToSaml2DateTimeString();
            var response = new Saml2Response(issuer, SignedXmlHelper.TestCert,
                new Uri(destination), null, identity);
            string after = DateTime.UtcNow.ToSaml2DateTimeString();

            var xml = response.XmlElement;

            xml.LocalName.Should().Be("Response");
            xml.NamespaceURI.Should().Be(Saml2Namespaces.Saml2PName);
            xml.Prefix.Should().Be("saml2p");
            xml["Issuer", Saml2Namespaces.Saml2Name].InnerText.Should().Be(issuer.Id);
            xml["Assertion", Saml2Namespaces.Saml2Name]
                ["Subject", Saml2Namespaces.Saml2Name]["NameID", Saml2Namespaces.Saml2Name]
                .InnerText.Should().Be(nameId);
            xml.GetAttribute("Destination").Should().Be(destination);
            xml.GetAttribute("ID").Should().NotBeNullOrWhiteSpace();
            xml.GetAttribute("Version").Should().Be("2.0");
            xml.GetAttribute("IssueInstant").Should().Match(
                i => i == before || i == after);
        }

        [TestMethod]
        public void Saml2Response_Xml_FromData_ContainsStatus_Success()
        {
            var identity = new ClaimsIdentity(new Claim[] 
            {
                new Claim(ClaimTypes.NameIdentifier, "JohnDoe") 
            });

            var response = new Saml2Response(new EntityId("issuer"), SignedXmlHelper.TestCert,
                new Uri("http://destination.example.com"), null, identity);

            var xml = response.XmlElement;

            var subject = xml["Status", Saml2Namespaces.Saml2PName];

            subject["StatusCode", Saml2Namespaces.Saml2PName].GetAttribute("Value")
                .Should().Be("urn:oasis:names:tc:SAML:2.0:status:Success");
        }

        [TestMethod]
        public void Saml2Response_Xml_FromData_ContainsInResponseTo()
        {
            var identity = new ClaimsIdentity(new Claim[] 
            {
                new Claim(ClaimTypes.NameIdentifier, "JohnDoe") 
            });

            var response = new Saml2Response(new EntityId("issuer"), SignedXmlHelper.TestCert,
                new Uri("http://destination.example.com"), new Saml2Id("InResponseToID"), identity);

            var xml = response.XmlElement;

            xml.GetAttribute("InResponseTo").Should().Be("InResponseToID");
        }

        [TestMethod]
        public void Saml2Response_Xml_FromData_ContainsAudienceRestriction()
        {
            var identity = new ClaimsIdentity(new Claim[]
            {
                new Claim(ClaimTypes.NameIdentifier, "JohnDoe")
            });

            var audience = "http://sp.example.com";

            var subject = new Saml2Response(
                new EntityId("issuer"),
                SignedXmlHelper.TestCert,
                new Uri("http://destination.example.com"),
                new Saml2Id("InResponseToID"),
                null,
                new Uri(audience),
                identity);

            var actual = subject.XmlElement;

            actual["Assertion", Saml2Namespaces.Saml2Name].Should().NotBeNull("Assertion element should be present")
                .And.Subject["Conditions", Saml2Namespaces.Saml2Name].Should().NotBeNull("Conditions element should be present")
                .And.Subject["AudienceRestriction", Saml2Namespaces.Saml2Name].Should().NotBeNull("AudienceRestriction element should be present")
                .And.Subject["Audience", Saml2Namespaces.Saml2Name].Should().NotBeNull("Audience element should be present")
                .And.Subject.InnerText.Should().Be(audience);
        }

        [TestMethod]
        public void Saml2Response_FromData_RelayState()
        {
            var subject = new Saml2Response(new EntityId("issuer"), null, null, null, "ABC123");

            subject.RelayState.Should().Be("ABC123");
        }

        [TestMethod]
        public void Saml2Response_ToXml()
        {
            string response = @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol"" ID=""Saml2Response_ToXml"" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z""><saml2p:Status><saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Requester"" /></saml2p:Status></saml2p:Response>";

            var subject = Saml2Response.Read(response).ToXml();

            subject.Should().Be(response);
        }

        [TestMethod]
        public void Saml2Response_MessageName()
        {
            var subject = new Saml2Response(new EntityId("issuer"), null, null, null);

            subject.MessageName.Should().Be("SAMLResponse");
        }

        [TestMethod]
        public void Saml2Response_GetClaims_ChecksSha256WhenEnabled()
        {
            Options.GlobalEnableSha256XmlSignatures();

            var signedResponse =
                @"<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
                    xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                    ID = """ + MethodBase.GetCurrentMethod().Name + @"_Response"" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                        <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                        <saml2p:Status>
                            <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                        </saml2p:Status>
                        <Assertion ID=""" + MethodBase.GetCurrentMethod().Name + @""" IssueInstant=""2015-03-13T20:43:33.466Z"" Version=""2.0"" xmlns=""urn:oasis:names:tc:SAML:2.0:assertion""><Issuer>https://idp.example.com</Issuer><Signature xmlns=""http://www.w3.org/2000/09/xmldsig#""><SignedInfo><CanonicalizationMethod Algorithm=""http://www.w3.org/2001/10/xml-exc-c14n#"" /><SignatureMethod Algorithm=""http://www.w3.org/2001/04/xmldsig-more#rsa-sha256"" /><Reference URI=""#Saml2Response_GetClaims_ChecksSha256WhenEnabled""><Transforms><Transform Algorithm=""http://www.w3.org/2000/09/xmldsig#enveloped-signature"" /><Transform Algorithm=""http://www.w3.org/2001/10/xml-exc-c14n#"" /></Transforms><DigestMethod Algorithm=""http://www.w3.org/2001/04/xmlenc#sha256"" /><DigestValue>8s5HDYeicqbNwESGyrvYYXinJeJJgl4t6O27KGE0ejc=</DigestValue></Reference></SignedInfo><SignatureValue>mS2TFErenJHyvUbyIDUItOvH6AavUNGg5zL3hVueWDGjhaft2mlWSlQIFm9ajVQKrZq2Q/V4oZYGTQ8muTfrhdCL3fyu453nEWcNgQ+gm1H1e89N75XWonfL+UQDl73O95SX0dD4DjqQAC4MlSwMOkwOR7GakhjPbSzRct7lFbRx/3k+TUZNj9rfV4uzlf79ebkw9EaaSfu0tR6bAfGyrefFaNTZs2NeRICfD/GKn7HRo9zSdVPBHfEW2UUy0x/aWREG4GgUs7qObWL4uhDZ6oyy5FbsRcrUJMiXCFNXA8dr9EtZ2VafHz3d4kJFLiq63xjqpjGk/ng2gP+47F/9Rw==</SignatureValue><KeyInfo><X509Data><X509Certificate>MIIDIzCCAg+gAwIBAgIQg7mOjTf994NAVxZu4jqXpzAJBgUrDgMCHQUAMCQxIjAgBgNVBAMTGUtlbnRvci5BdXRoU2VydmljZXMuVGVzdHMwHhcNMTMwOTI1MTMzNTQ0WhcNMzkxMjMxMjM1OTU5WjAkMSIwIAYDVQQDExlLZW50b3IuQXV0aFNlcnZpY2VzLlRlc3RzMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAwVGpfvK9N//MnA5Jo1q2liyPR24406Dp25gv7LB3HK4DWgqsb7xXM6KIV/WVOyCV2g/O1ErBlB+HLhVZ4XUJvbqBbgAJqFO+TZwcCIe8u4nTEXeU660FdtkKClA17sbtMrAGdDfOPwVBHSuavdHeD7jHNI4RUDGKnEW13/0EvnHDilIetwODRxrX/+41R24sJThFbMczByS3OAL2dcIxoAynaGeM90gXsVYow1QhJUy21+cictikb7jW4mW6dvFCBrWIceom9J295DcQIHoxJy5NoZwMir/JV00qs1wDVoN20Ve1DC5ImwcG46XPF7efQ44yLh2j5Yexw+xloA81dwIDAQABo1kwVzBVBgNVHQEETjBMgBAWIahoZhXVUogbAqkS7zwfoSYwJDEiMCAGA1UEAxMZS2VudG9yLkF1dGhTZXJ2aWNlcy5UZXN0c4IQg7mOjTf994NAVxZu4jqXpzAJBgUrDgMCHQUAA4IBAQA2aGzmuKw4AYXWMhrGj5+i8vyAoifUn1QVOFsUukEA77CrqhqqaWFoeagfJp/45vlvrfrEwtF0QcWfmO9w1VvHwm7sk1G/cdYyJ71sU+llDsdPZm7LxQvWZYkK+xELcinQpSwt4ExavS+jLcHoOYHYwIZMBn3U8wZw7Kq29oGnoFQz7HLCEl/G9i3QRyvFITNlWTjoScaqMjHTzq6HCMaRsL09DLcY3KB+cedfpC0/MBlzaxZv0DctTulyaDfM9DCYOyokGN/rQ6qkAR0DDm8fVwknbJY7kURXNGoUetulTb5ow8BvD1gncOaYHSD0kbHZG+bLsUZDFatEr2KW8jbG</X509Certificate></X509Data></KeyInfo></Signature><Subject><NameID>SomeUser</NameID><SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" /></Subject><Conditions NotOnOrAfter=""2100-01-01T05:00:00.000Z"" /></Assertion>
                    </saml2p:Response>";

            var spOptions = StubFactory.CreateSPOptions();
            var options = new Options(spOptions);
            var idp = new IdentityProvider(new EntityId("https://idp.example.com"), spOptions) { AllowUnsolicitedAuthnResponse = true };
            idp.SigningKeys.AddConfiguredKey(SignedXmlHelper.TestKeySignOnly);
            options.IdentityProviders.Add(idp);

            Action a = () => Saml2Response.Read(signedResponse).GetClaims(options);
            a.ShouldNotThrow();
        }

        [TestMethod]
        public void Saml2Response_GetClaims_ValidatesIdpCertificateIfConfigured()
        {
            var options = StubFactory.CreateOptions();

            options.SPOptions.ValidateCertificates = true;

            var responseXml = 
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                <saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
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

            responseXml = SignedXmlHelper.SignXml(responseXml);

            Saml2Response.Read(responseXml).Invoking(
                r => r.GetClaims(options))
                .ShouldThrow<InvalidSignatureException>()
                .And.Message.Should().Be("The signature was valid, but the verification of the certificate failed. Is it expired or revoked? Are you sure you really want to enable ValidateCertificates (it's normally not needed)?");
        }

        [TestMethod]
        public void Saml2Response_SessionNotOnOrAfter_ExtractedFromMessage()
        {
            var response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                <saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + $@"1""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                    <saml2:AuthnStatement AuthnInstant=""{DateTime.UtcNow.ToSaml2DateTimeString()}"" SessionNotOnOrAfter = ""2050-01-01T00:00:00Z"">
                        <saml2:AuthnContext>
                            <saml2:AuthnContextClassRef>urn:oasis:names:tc:SAML:2.0:ac:classes:PasswordProtectedTransport</saml2:AuthnContextClassRef>
                        </saml2:AuthnContext>
                    </saml2:AuthnStatement>
                </saml2:Assertion>
                <saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + $@"2""
                IssueInstant=""2013-09-25T00:00:00Z"">
                    <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                    <saml2:Subject>
                        <saml2:NameID>SomeOtherUser</saml2:NameID>
                        <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"" />
                    </saml2:Subject>
                    <saml2:Conditions NotOnOrAfter=""2100-01-01T00:00:00Z"" />
                    <saml2:AuthnStatement AuthnInstant=""{DateTime.UtcNow.ToSaml2DateTimeString()}"" SessionNotOnOrAfter = ""2051-01-01T00:00:00Z"">
                        <saml2:AuthnContext>
                            <saml2:AuthnContextClassRef>urn:oasis:names:tc:SAML:2.0:ac:classes:PasswordProtectedTransport</saml2:AuthnContextClassRef>
                        </saml2:AuthnContext>
                    </saml2:AuthnStatement>
                </saml2:Assertion>
            </saml2p:Response>";

            var subject = Saml2Response.Read(SignedXmlHelper.SignXml(response));

            subject.GetClaims(StubFactory.CreateOptions());

            subject.SessionNotOnOrAfter.Should().Be(new DateTime(2050, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }

        [TestMethod]
        public void Saml2Response_SessionNotOnOrAfter_ThrowsIfCalledBeforeGetClaims()
        {
            var response =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
            <saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol""
            xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
            ID = """ + MethodBase.GetCurrentMethod().Name + @""" Version=""2.0"" IssueInstant=""2013-01-01T00:00:00Z"">
                <saml2:Issuer>https://idp.example.com</saml2:Issuer>
                <saml2p:Status>
                    <saml2p:StatusCode Value=""urn:oasis:names:tc:SAML:2.0:status:Success"" />
                </saml2p:Status>
                <saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion""
                Version=""2.0"" ID=""" + MethodBase.GetCurrentMethod().Name + $@"1""
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

            var subject = Saml2Response.Read(SignedXmlHelper.SignXml(response));

            subject.Invoking(s => { var value = s.SessionNotOnOrAfter; })
                .ShouldThrow<InvalidOperationException>()
                .WithMessage("*GetClaims*");
        }
    }
}
