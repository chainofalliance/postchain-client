using Cryptography.ECDSA;
using System;
using RSG;

namespace Chromia.PostchainClient.GTX
{
    public class Transaction
    {
        public Gtx GtxObject;
        private RESTClient RestClient;

        public Transaction(Gtx gtx, RESTClient restClient)
        {
            this.GtxObject = gtx;
            this.RestClient = restClient;
        }

        public void Sign(byte[] privKey, byte[] pubKey)
        {
            byte[] pub = pubKey;
            if(pubKey == null)
            {
                pub = Secp256K1Manager.GetPublicKey(privKey, false);
            }
            this.GtxObject.Sign(privKey, pub);
        }

        public string GetTxRID()
        {
            return System.Text.Encoding.Default.GetString(Util.Sha256(this.GetBufferToSign()));
        }

        public byte[] GetBufferToSign()
        {
            return this.GtxObject.GetBufferToSign();
        }

        public void AddSignature(byte[] pubKey, byte[] signature)
        {
            this.GtxObject.AddSignature(pubKey, signature);
        }

        public void AddOperation(string name, dynamic[] args)
        {
            this.GtxObject.AddTransactionToGtx(name, args);
        }

        public Promise<Promise<string>> PostAndWaitConfirmation()
        {
            return this.RestClient.PostAndWaitConfirmation(
                this.GtxObject.Serialize(), this.GetTxRID()
            );
        }

        public void Send(Action<string, dynamic> callback)
        {
            var gtxBytes = this.GtxObject.Serialize();
            this.RestClient.PostTransaction(gtxBytes, callback);
            this.GtxObject = null;
        }

        public string Encode()
        {
            return this.GtxObject.Serialize();
        }
    }
}