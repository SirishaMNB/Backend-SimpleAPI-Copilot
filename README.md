# Backend-SimpleAPI-Copilot
Activity for a simple API with Copilot
Took help of Copilot - 

CRUD API for User management - Copilot suggested minimal API's, with DB, with in memory-lists
Understanding the possible errors in the code with copilot's suggesstion and updated code as per suggestions by copilot.
Suggested Edge case scenarios. Attached Requests.http for all test cases including ones suggested by copilot.

To enforce standardized error handling across all endpoints, the best practice in ASP.NET Core minimal APIs is to:

-  Add a global exception handling middleware
-  Return consistent response format (success + error)
-  Avoid inline string/primitive errors in endpoints
-  entralize validation + error mapping

using token-based authentication (JWT)

Order matters 
Exception → Auth → Authorization → Endpoints
