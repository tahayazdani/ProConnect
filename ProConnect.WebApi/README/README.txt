--Application sections

- SignUp (LinkedIn)
	- user should be able to authenticate with their LinkedIn account, then their photo, full name, email
	and their linkedIn url (not sure about that) will be fetched. then our system will register the user on our end.



- SignIn(LinkedIn)
	- user should be able to login with their LinkedIn profile, we fetech data and compare with our system, if the user exist
	 then they can be authenticated.


- Define search critria
	- for the very first time, the user need to set their match critria such as location(city, state, country), 
	comopany, age, interest, ... (user can update it later on)



- Match recommondation (search engine)
	- based on the filter our engine always looks for the best match for the user and store it in Redis cache
	to speed up time.

- Search user

- Notification

- Send request (following & Follower)

- Send messages


- Account setting
	- payment (loads all transactions and payments)
	- Subscription (shows all models and current model the user on)
	- update match critria (it must load the existing one and let user to modify it)



