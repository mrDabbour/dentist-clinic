# Patient billing

Staff confirms a pending appointment. That transaction creates an in-website patient notification independently of invoice configuration. A global patient bell checks for updates every five seconds and shows a red unread count and persistent alert. Patients click **Enable sound** once in the current page session to allow a chime for new confirmations. Opening a notification marks it read and opens the booking details; closing the bell does not mark it read. Read notifications remain in history. The booking page refreshes pending appointment status every five seconds. Email/SMS delivery is not configured.

Invoices snapshot service price, customer and supplier details. Subsequent service price changes do not alter issued invoices. Only the owning patient and Admin/Receptionist can access billing. Bank transfers are instructions for a payment made in the patient's banking app, not an automatic bank debit.

## Required configuration

The clinic selected GST-inclusive pricing. Docker defaults to `Inclusive`: existing listed prices remain the full amount payable. Bank payment requests can be issued before a GST number is supplied; the website labels these **Payment request**, not **Tax invoice**. Set these Docker environment variables locally, then rebuild the API:

- `BILLING_GST_PRICE_MODE`: `Inclusive`, `Exclusive`, or `NotRegistered`. Do not select a registered mode unless the clinic is GST registered.
- `BILLING_GST_NUMBER`: actual registration number to complete GST invoice details. Existing payment requests gain the supplier GST number when reopened after it is configured, without changing their amounts.
- `BILLING_SUPPLIER_NAME`, `BILLING_SUPPLIER_ADDRESS`: clinic's legal supplier details.
- `BILLING_FRONTEND_ORIGIN`: HTTPS public website origin (localhost allowed for development).

NZ GST is 15%. Inclusive prices use 15/115 to extract GST; Exclusive prices add 15%; NotRegistered invoices charge no GST. Amounts round to two decimal places. See [IRD charging GST](https://www.ird.govt.nz/gst/charging-gst) and [taxable supply information](https://www.ird.govt.nz/gst/tax-invoices-for-gst).

Bank instructions use the supplied account holder **Mohammed Dabboor**, account **15-39530983855000**. The application does not verify ownership or account validity. Patients use their invoice number as payment reference. Reporting a transfer sets AwaitingVerification. Admin/Receptionist must check the actual bank statement, enter its transaction reference, and verify in **Invoice payments**. Only verification marks it Paid.

## PayPal setup

Set `PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET`, optional `PAYPAL_MERCHANT_ID`, and `PAYPAL_WEBHOOK_ID` locally. Keep secrets out of source control. Sandbox is the default (`PAYPAL_LIVE=false`). Use credentials and webhook ID from the same PayPal app/environment. Register `https://YOUR_API/api/billing/paypal/webhook` for `PAYMENT.CAPTURE.COMPLETED`. Use a real sandbox buyer/merchant test before enabling live credentials and `PAYPAL_LIVE=true`.

Orders are created server-side in NZD. A browser approval/redirect never marks an invoice paid: the API captures and verifies the order, capture, invoice ID, currency and amount. Signed webhooks recover completed payments when a buyer closes the browser. Duplicate notifications are idempotent. Additional payments on an already-paid invoice return a conflict for clinic investigation. Refunds, chargebacks and bank reconciliation are manual clinic operations; automated handling is not implemented.

Provider API reference: [PayPal Orders v2](https://developer.paypal.com/api/orders/v2/). Webhook verification: [PayPal REST webhooks](https://developer.paypal.com/api/rest/webhooks/rest/).

Migration: `20261001050532_AddInvoicesAndPatientNotifications`. No existing appointments are automatically charged or retrospectively invoiced. Invoices are issued when a confirmed appointment's invoice page is opened.
