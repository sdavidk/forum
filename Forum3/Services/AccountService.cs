﻿using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Blob;
using Forum3.Controllers;
using Forum3.Helpers;
using Forum3.Interfaces.Users;

using DataModels = Forum3.Models.DataModels;
using InputModels = Forum3.Models.InputModels;
using ServiceModels = Forum3.Models.ServiceModels;
using ViewModels = Forum3.Models.ViewModels.Account;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Forum3.Services {
	public class AccountService {
		DataModels.ApplicationDbContext DbContext { get; }
		SiteSettingsService SiteSettingsService { get; }
		CloudBlobClient CloudBlobClient { get; }
		ServiceModels.ContextUser ContextUser { get; }
		UserManager<DataModels.ApplicationUser> UserManager { get; }
		SignInManager<DataModels.ApplicationUser> SignInManager { get; }
		IEmailSender EmailSender { get; }
		IUrlHelper UrlHelper { get; }
		ILogger Logger { get; }

		public AccountService(
			DataModels.ApplicationDbContext dbContext,
			SiteSettingsService siteSettingsService,
			CloudBlobClient cloudBlobClient,
			ContextUserFactory contextUserFactory,
			UserManager<DataModels.ApplicationUser> userManager,
			SignInManager<DataModels.ApplicationUser> signInManager,
			IActionContextAccessor actionContextAccessor,
			IUrlHelperFactory urlHelperFactory,
			IEmailSender emailSender,
			ILogger<AccountService> logger
		) {
			DbContext = dbContext;
			SiteSettingsService = siteSettingsService;
			CloudBlobClient = cloudBlobClient;
			ContextUser = contextUserFactory.GetContextUser();
			UserManager = userManager;
			SignInManager = signInManager;
			EmailSender = emailSender;
			UrlHelper = urlHelperFactory.GetUrlHelper(actionContextAccessor.ActionContext);
			Logger = logger;
		}

		public async Task<ViewModels.LoginPage> LoginPage() {
			await SignOut();

			var viewModel = new ViewModels.LoginPage();
			return viewModel;
		}

		public async Task<ViewModels.LoginPage> LoginPage(InputModels.LoginInput input) {
			var viewModel = await LoginPage();

			viewModel.Email = input.Email;
			viewModel.RememberMe = input.RememberMe;

			return viewModel;
		}

		public async Task<ServiceModels.ServiceResponse> Login(InputModels.LoginInput input) {
			var serviceResponse = new ServiceModels.ServiceResponse();

			var result = await SignInManager.PasswordSignInAsync(input.Email, input.Password, input.RememberMe, lockoutOnFailure: false);

			if (result.IsLockedOut) {
				Logger.LogWarning($"User account locked out '{input.Email}'.");
				serviceResponse.RedirectPath = UrlHelper.Action(nameof(Account.Lockout), nameof(Account));
			}
			else if (!result.Succeeded) {
				Logger.LogWarning($"Invalid login attempt for account '{input.Email}'.");
				serviceResponse.Error(string.Empty, "Invalid login attempt.");
			}
			else
				Logger.LogInformation($"User logged in '{input.Email}'.");

			return serviceResponse;
		}

		public async Task<ViewModels.DetailsPage> DetailsPage(string displayName) {
			if (string.IsNullOrEmpty(displayName))
				displayName = ContextUser.ApplicationUser.DisplayName;

			var userRecord = await DbContext.Users.SingleOrDefaultAsync(u => u.DisplayName == displayName);

			if (userRecord == null) {
				var message = $"No record found with the display name '{displayName}'";
				Logger.LogWarning(message);
				throw new ApplicationException("You hackin' bro?");
			}

			CanEdit(userRecord.Id);

			var viewModel = new ViewModels.DetailsPage {
				AvatarPath = userRecord.AvatarPath,
				Id = userRecord.Id,
				DisplayName = userRecord.DisplayName,
				Email = userRecord.Email,
				EmailConfirmed = userRecord.EmailConfirmed
			};

			return viewModel;
		}

		public async Task<ViewModels.DetailsPage> DetailsPage(InputModels.UpdateAccountInput input) {
			var userRecord = await DbContext.Users.FindAsync(input.Id);

			if (userRecord == null) {
				var message = $"No record found with the display name '{input.DisplayName}'";
				Logger.LogWarning(message);
				throw new ApplicationException("You hackin' bro?");
			}

			CanEdit(userRecord.Id);

			var viewModel = new ViewModels.DetailsPage {
				Id = input.Id,
				DisplayName = input.DisplayName,
				Email = input.Email,
				EmailConfirmed = userRecord.EmailConfirmed
			};

			return viewModel;
		}

		public async Task<ServiceModels.ServiceResponse> UpdateAccount(InputModels.UpdateAccountInput input) {
			var serviceResponse = new ServiceModels.ServiceResponse();

			if (!await UserManager.CheckPasswordAsync(ContextUser.ApplicationUser, input.Password)) {
				var message = $"Invalid password for '{input.DisplayName}'.";
				serviceResponse.Error(nameof(InputModels.UpdateAccountInput.Password), message);
				Logger.LogWarning(message);
			}

			var userRecord = await DbContext.Users.FindAsync(input.Id);

			if (userRecord == null) {
				var message = $"No user record found for '{input.DisplayName}'.";
				serviceResponse.Error(string.Empty, message);
				Logger.LogCritical(message);
			}

			CanEdit(userRecord.Id);

			var account = await UserManager.FindByIdAsync(input.Id);

			if (account == null) {
				var message = $"No user account found for '{input.DisplayName}'.";
				serviceResponse.Error(string.Empty, message);
				Logger.LogCritical(message);
			}

			if (!serviceResponse.Success)
				return serviceResponse;

			if (input.DisplayName != userRecord.DisplayName) {
				userRecord.DisplayName = input.DisplayName;
				DbContext.Entry(userRecord).State = EntityState.Modified;

				Logger.LogInformation($"Display name was modified by '{ContextUser.ApplicationUser.DisplayName}' for account '{userRecord.DisplayName}'.");
			}

			await DbContext.SaveChangesAsync();

			if (input.Email != userRecord.Email) {
				serviceResponse.RedirectPath = UrlHelper.Action(nameof(Account.Details), nameof(Account), new { id = input.DisplayName });

				var identityResult = await UserManager.SetEmailAsync(account, input.Email);

				if (!identityResult.Succeeded) {
					foreach (var error in identityResult.Errors) {
						Logger.LogError($"Error modifying email by '{ContextUser.ApplicationUser.DisplayName}' from '{userRecord.Email}' to '{input.Email}'. Message: {error.Description}");
						serviceResponse.Error(error.Description);
					}

					return serviceResponse;
				}

				identityResult = await UserManager.SetUserNameAsync(account, input.Email);

				if (!identityResult.Succeeded) {
					foreach (var error in identityResult.Errors) {
						Logger.LogError($"Error modifying username by '{ContextUser.ApplicationUser.DisplayName}' from '{userRecord.Email}' to '{input.Email}'. Message: {error.Description}");
						serviceResponse.Error(error.Description);
					}

					return serviceResponse;
				}

				Logger.LogInformation($"Email address was modified by '{ContextUser.ApplicationUser.DisplayName}' from '{userRecord.Email}' to '{input.Email}'.");

				var code = await UserManager.GenerateEmailConfirmationTokenAsync(account);

				if (EmailSender.Ready) {
					var callbackUrl = EmailConfirmationLink(account.Id, code);

					await EmailSender.SendEmailConfirmationAsync(input.Email, callbackUrl);

					if (account.Id == ContextUser.ApplicationUser.Id)
						await SignOut();
				}
				else {
					identityResult = await UserManager.ConfirmEmailAsync(account, code);

					if (!identityResult.Succeeded) {
						foreach (var error in identityResult.Errors) {
							Logger.LogError($"Error confirming '{account.Email}'. Message: {error.Description}");
							serviceResponse.Error(string.Empty, error.Description);
						}
					}
					else
						Logger.LogInformation($"User confirmed email '{account.Email}'.");
				}

				return serviceResponse;
			}

			// This allows admins to reset user passwords as well, assuming they don't set the password to the same thing as theirs.
			if (!string.IsNullOrEmpty(input.NewPassword) && input.Password != input.NewPassword) {
				var identityResult = await UserManager.ChangePasswordAsync(account, input.Password, input.NewPassword);

				if (!identityResult.Succeeded) {
					foreach (var error in identityResult.Errors) {
						Logger.LogError($"Error modifying password by '{ContextUser.ApplicationUser.DisplayName}' for '{userRecord.DisplayName}'. Message: {error.Description}");
						serviceResponse.Error(nameof(InputModels.UpdateAccountInput.NewPassword), error.Description);
					}
				}
				else if (account.Id == ContextUser.ApplicationUser.Id) {
					Logger.LogInformation($"Password was modified by '{ContextUser.ApplicationUser.DisplayName}' for '{userRecord.DisplayName}'.");
					await SignOut();
					serviceResponse.RedirectPath = UrlHelper.Action(nameof(Login));
					return serviceResponse;
				}
			}

			if (serviceResponse.Success)
				serviceResponse.RedirectPath = UrlHelper.Action(nameof(Account.Details), nameof(Account), new { id = input.DisplayName });

			return serviceResponse;
		}

		public async Task<ServiceModels.ServiceResponse> UpdateAvatar(InputModels.UpdateAvatarInput input) {
			var serviceResponse = new ServiceModels.ServiceResponse();

			var userRecord = await DbContext.Users.FindAsync(input.Id);

			if (userRecord == null) {
				var message = $"No user record found for '{input.DisplayName}'.";
				serviceResponse.Error(string.Empty, message);
				Logger.LogCritical(message);
			}

			CanEdit(input.Id);

			if (!serviceResponse.Success)
				return serviceResponse;

			var allowedExtensions = new[] { ".gif", ".jpg", ".png" };
			var extension = Path.GetExtension(input.NewAvatar.FileName);

			if (!allowedExtensions.Contains(extension))
				serviceResponse.Error(nameof(input.NewAvatar), "Your avatar must end with .gif, .jpg, or .png");

			if (!serviceResponse.Success)
				return serviceResponse;

			var container = CloudBlobClient.GetContainerReference("avatars");

			if (await container.CreateIfNotExistsAsync())
				await container.SetPermissionsAsync(new BlobContainerPermissions { PublicAccess = BlobContainerPublicAccessType.Blob });

			var blobReference = container.GetBlockBlobReference($"avatar{userRecord.Id}.png");
			blobReference.Properties.ContentType = "image/png";

			using (var inputStream = input.NewAvatar.OpenReadStream()) {
				inputStream.Position = 0;

				using (var src = Image.FromStream(inputStream)) {
					var largestDimension = src.Width > src.Height ? src.Width : src.Height;
					var avatarMax = Constants.Defaults.AvatarSize;

					if (largestDimension > avatarMax || extension != ".png") {
						var ratio = (double)avatarMax / largestDimension;

						var destinationWidth = Convert.ToInt32(src.Width * ratio);
						var destinationHeight = Convert.ToInt32(src.Height * ratio);

						using (var dst = new Bitmap(destinationWidth, destinationHeight)) {
							using (var g = Graphics.FromImage(dst)) {
								g.SmoothingMode = SmoothingMode.AntiAlias;
								g.InterpolationMode = InterpolationMode.HighQualityBicubic;
								g.DrawImage(src, 0, 0, dst.Width, dst.Height);
							}

							var ms = new MemoryStream();
							dst.Save(ms, ImageFormat.Png);
							ms.Position = 0;

							await blobReference.UploadFromStreamAsync(ms);
						}
					}
					else
						await blobReference.UploadFromStreamAsync(inputStream);
				}
			}

			userRecord.AvatarPath = blobReference.Uri.AbsoluteUri;
			DbContext.Entry(userRecord).State = EntityState.Modified;

			await DbContext.SaveChangesAsync();

			Logger.LogInformation($"Avatar was modified by '{ContextUser.ApplicationUser.DisplayName}' for account '{userRecord.DisplayName}'.");

			if (serviceResponse.Success)
				serviceResponse.RedirectPath = UrlHelper.Action(nameof(Account.Details), nameof(Account), new { id = input.DisplayName });

			return serviceResponse;
		}

		public async Task<ViewModels.RegisterPage> RegisterPage() {
			await SignOut();

			var viewModel = new ViewModels.RegisterPage();
			return viewModel;
		}

		public async Task<ViewModels.RegisterPage> RegisterPage(InputModels.RegisterInput input) {
			var viewModel = await RegisterPage();

			viewModel.DisplayName = input.DisplayName;
			viewModel.Email = input.Email;
			viewModel.ConfirmEmail = input.ConfirmEmail;
			viewModel.Password = input.Password;
			viewModel.ConfirmPassword = input.ConfirmPassword;

			return viewModel;
		}

		public async Task<ServiceModels.ServiceResponse> Register(InputModels.RegisterInput input) {
			var serviceResponse = new ServiceModels.ServiceResponse();

			var user = new DataModels.ApplicationUser {
				DisplayName = input.DisplayName,
				UserName = input.Email,
				Email = input.Email
			};

			var identityResult = await UserManager.CreateAsync(user, input.Password);

			if (identityResult.Succeeded) {
				Logger.LogInformation($"User created a new account with password '{input.Email}'.");

				var code = await UserManager.GenerateEmailConfirmationTokenAsync(user);
				var callbackUrl = EmailConfirmationLink(user.Id, code);

				if (!EmailSender.Ready) {
					serviceResponse.RedirectPath = callbackUrl;
					return serviceResponse;
				}

				await EmailSender.SendEmailConfirmationAsync(input.Email, callbackUrl);
			}
			else {
				foreach (var error in identityResult.Errors) {
					Logger.LogError($"Error registering '{input.Email}'. Message: {error.Description}");
					serviceResponse.Error(string.Empty, error.Description);
				}
			}

			return serviceResponse;
		}

		public async Task SignOut() => await SignInManager.SignOutAsync();

		public async Task<ServiceModels.ServiceResponse> SendVerificationEmail() {
			var serviceResponse = new ServiceModels.ServiceResponse();

			var code = await UserManager.GenerateEmailConfirmationTokenAsync(ContextUser.ApplicationUser);
			var callbackUrl = EmailConfirmationLink(ContextUser.ApplicationUser.Id, code);
			var email = ContextUser.ApplicationUser.Email;

			if (!EmailSender.Ready) {
				serviceResponse.RedirectPath = callbackUrl;
				return serviceResponse;
			}

			await EmailSender.SendEmailConfirmationAsync(email, callbackUrl);

			serviceResponse.Message = "Verification email sent. Please check your email.";
			return serviceResponse;
		}

		public async Task<ServiceModels.ServiceResponse> ConfirmEmail(InputModels.ConfirmEmailInput input) {
			var serviceResponse = new ServiceModels.ServiceResponse();

			var account = await UserManager.FindByIdAsync(input.UserId);

			if (account == null)
				serviceResponse.Error(string.Empty, $"Unable to load account '{input.UserId}'.");

			if (serviceResponse.Success) {
				var identityResult = await UserManager.ConfirmEmailAsync(account, input.Code);

				if (!identityResult.Succeeded) {
					foreach (var error in identityResult.Errors) {
						Logger.LogError($"Error confirming '{account.Email}'. Message: {error.Description}");
						serviceResponse.Error(string.Empty, error.Description);
					}
				}
				else
					Logger.LogInformation($"User confirmed email '{account.Id}'.");
			}

			return serviceResponse;
		}

		public async Task<ViewModels.ForgotPasswordPage> ForgotPasswordPage() {
			await SignOut();

			var viewModel = new ViewModels.ForgotPasswordPage();
			return viewModel;
		}

		public async Task<ViewModels.ForgotPasswordPage> ForgotPasswordPage(InputModels.ForgotPasswordInput input) {
			var viewModel = await ForgotPasswordPage();
			viewModel.Email = input.Email;
			return viewModel;
		}

		public async Task<ServiceModels.ServiceResponse> ForgotPassword(InputModels.ForgotPasswordInput input) {
			var serviceResponse = new ServiceModels.ServiceResponse();

			var account = await UserManager.FindByNameAsync(input.Email);

			if (account != null && await UserManager.IsEmailConfirmedAsync(account)) {
				var code = await UserManager.GeneratePasswordResetTokenAsync(account);
				var callbackUrl = ResetPasswordCallbackLink(account.Id, code);

				if (!EmailSender.Ready) {
					serviceResponse.RedirectPath = callbackUrl;
					return serviceResponse;
				}

				await EmailSender.SendEmailAsync(input.Email, "Reset Password", $"Please reset your password by clicking here: <a href='{callbackUrl}'>link</a>");
			}

			serviceResponse.RedirectPath = UrlHelper.Action(nameof(Account.ForgotPasswordConfirmation));
			return serviceResponse;
		}

		public async Task<ViewModels.ResetPasswordPage> ResetPasswordPage(string code) {
			code.ThrowIfNullOrEmpty(nameof(code));

			await SignOut();

			var viewModel = new ViewModels.ResetPasswordPage {
				Code = code
			};

			return viewModel;
		}

		public async Task<ServiceModels.ServiceResponse> ResetPassword(InputModels.ResetPasswordInput input) {
			var serviceResponse = new ServiceModels.ServiceResponse();

			var account = await UserManager.FindByEmailAsync(input.Email);

			if (account != null) {
				var identityResult = await UserManager.ResetPasswordAsync(account, input.Code, input.Password);

				if (!identityResult.Succeeded) {
					foreach (var error in identityResult.Errors)
						Logger.LogError($"Error resetting password for '{account.Email}'. Message: {error.Description}");
				}
				else
					Logger.LogInformation($"Password was reset for '{account.Email}'.");
			}

			serviceResponse.RedirectPath = nameof(Account.ResetPasswordConfirmation);
			return serviceResponse;
		}

		public string EmailConfirmationLink(string userId, string code) => UrlHelper.AbsoluteAction(nameof(Account.ConfirmEmail), nameof(Account), new { userId, code });
		public string ResetPasswordCallbackLink(string userId, string code) => UrlHelper.AbsoluteAction(nameof(Account.ResetPassword), nameof(Account), new { userId, code });

		void CanEdit(string userId) {
			if (userId == ContextUser.ApplicationUser.Id || ContextUser.IsAdmin)
				return;

			Logger.LogWarning($"A user tried to edit another user's profile. {ContextUser.ApplicationUser.DisplayName}");

			throw new ApplicationException("You hackin' bro?");
		}
	}
}